using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.CodeFixes.Formatting;
using NetAgents.Analyzers.Formatting;

namespace NetAgents.Analyzers.Tests.Infrastructure;

internal static class CodeFixTestHarness
{
    public static async Task<string> FixSpacing(string source, bool fixAll)
    {
        return await Fix(source, new StatementSpacingAnalyzer(), new StatementSpacingCodeFixProvider(), fixAll).ConfigureAwait(continueOnCapturedContext: false);
    }

    public static async Task<string> FixBraces(string source, bool fixAll)
    {
        return await Fix(source, new ControlFlowBracesAnalyzer(), new ControlFlowBracesCodeFixProvider(), fixAll).ConfigureAwait(continueOnCapturedContext: false);
    }

    private static async Task<string> Fix(string source, DiagnosticAnalyzer analyzer, CodeFixProvider provider, bool fixAll)
    {
        using AdhocWorkspace workspace = new();

        SyntaxNode sourceRoot = await CSharpSyntaxTree.ParseText(source).GetRootAsync().ConfigureAwait(continueOnCapturedContext: false);
        OutputKind outputKind = sourceRoot.ChildNodes().OfType<GlobalStatementSyntax>().Any() ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary;

        Project project = workspace.AddProject(name: "CodeFixTests", LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp14))
            .WithCompilationOptions(new CSharpCompilationOptions(outputKind, allowUnsafe: true))
            .AddMetadataReferences(AnalyzerTestHarness.References);

        Assert.True(workspace.TryApplyChanges(project.Solution));
        Document document = workspace.AddDocument(project.Id, name: "ExampleType.cs", SourceText.From(source, Encoding.UTF8));
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnostics(document, analyzer).ConfigureAwait(continueOnCapturedContext: false);

        Assert.False(diagnostics.IsEmpty);
        List<CodeAction> actions = [];
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostics[index: 0],
            (action, associatedDiagnostics) => actions.Add(action), CancellationToken.None)).ConfigureAwait(continueOnCapturedContext: false);
        CodeAction action = Assert.Single(actions);

        if (fixAll)
        {
            FixAllContext context = new(document, provider, FixAllScope.Document, action.EquivalenceKey,
                provider.FixableDiagnosticIds.ToArray(), new CodeFixDiagnosticProvider(analyzer), CancellationToken.None);

            FixAllProvider? fixAllProvider = provider.GetFixAllProvider();
            Assert.NotNull(fixAllProvider);
            CodeAction? fixAllAction = await fixAllProvider.GetFixAsync(context).ConfigureAwait(continueOnCapturedContext: false);
            Assert.NotNull(fixAllAction);
            action = fixAllAction;
        }

        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
        ApplyChangesOperation changes = Assert.Single(operations.OfType<ApplyChangesOperation>());
        Document? changedDocument = changes.ChangedSolution.GetDocument(document.Id);
        Assert.NotNull(changedDocument);
        ImmutableArray<Diagnostic> remaining = await GetDiagnostics(changedDocument, analyzer).ConfigureAwait(continueOnCapturedContext: false);

        if (fixAll)
            Assert.True(remaining.IsEmpty);

        return (await changedDocument.GetTextAsync().ConfigureAwait(continueOnCapturedContext: false)).ToString();
    }

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnostics(Document document, DiagnosticAnalyzer analyzer)
    {
        Compilation? compilation = await document.Project.GetCompilationAsync().ConfigureAwait(continueOnCapturedContext: false);
        Assert.NotNull(compilation);
        Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
