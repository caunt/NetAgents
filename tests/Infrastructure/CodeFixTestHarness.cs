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
using NetAgents.Analyzers.CodeFixes.Ordering;
using NetAgents.Analyzers.CodeFixes.Readability;
using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Ordering;
using NetAgents.Analyzers.Readability;

namespace NetAgents.Analyzers.Tests.Infrastructure;

internal static class CodeFixTestHarness
{
    public static async Task AssertNoLiteralArgumentNameFix(string source)
    {
        using AdhocWorkspace workspace = new();

        Document document = CreateDocument(workspace, source);

        ImmutableArray<Diagnostic> diagnostics = await GetDiagnostics(document, new ExplicitLiteralArgumentNameAnalyzer())
            .ConfigureAwait(continueOnCapturedContext: false);

        Assert.False(diagnostics.IsEmpty);
        ExplicitLiteralArgumentNameCodeFixProvider provider = new();
        List<CodeAction> actions = [];

        foreach (Diagnostic diagnostic in diagnostics)
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, associatedDiagnostics) => actions.Add(action), CancellationToken.None)).ConfigureAwait(continueOnCapturedContext: false);

        Assert.Empty(actions);
    }

    public static async Task<string> FixArguments(string source, bool fixAll)
    {
        return await Fix(source, new ArgumentLayoutAnalyzer(), new ArgumentLayoutCodeFixProvider(), fixAll).ConfigureAwait(continueOnCapturedContext: false);
    }

    public static async Task<string> FixBlockPadding(string source, bool fixAll)
    {
        return await Fix(source, new BlockPaddingAnalyzer(), new BlockPaddingCodeFixProvider(), fixAll).ConfigureAwait(continueOnCapturedContext: false);
    }

    public static async Task<string> FixBraces(string source, bool fixAll)
    {
        return await Fix(source, new ControlFlowBracesAnalyzer(), new ControlFlowBracesCodeFixProvider(), fixAll).ConfigureAwait(continueOnCapturedContext: false);
    }

    public static async Task<string> FixLiteralArgumentNames(string source, bool fixAll)
    {
        return await Fix(source, new ExplicitLiteralArgumentNameAnalyzer(), new ExplicitLiteralArgumentNameCodeFixProvider(), fixAll)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    public static async Task<string> FixMemberOrdering(string source, bool fixAll)
    {
        return await Fix(source, new MemberOrderingAnalyzer(), new MemberOrderingCodeFixProvider(), fixAll).ConfigureAwait(continueOnCapturedContext: false);
    }

    public static async Task<string> FixMethodSpacing(string source, bool fixAll)
    {
        return await Fix(source, new MethodSpacingAnalyzer(), new MethodSpacingCodeFixProvider(), fixAll).ConfigureAwait(continueOnCapturedContext: false);
    }

    public static async Task<string> FixSpacing(string source, bool fixAll)
    {
        return await Fix(source, new StatementSpacingAnalyzer(), new StatementSpacingCodeFixProvider(), fixAll).ConfigureAwait(continueOnCapturedContext: false);
    }

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnostics(Document document, DiagnosticAnalyzer analyzer)
    {
        Compilation? compilation = await document.Project.GetCompilationAsync().ConfigureAwait(continueOnCapturedContext: false);
        Assert.NotNull(compilation);
        Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string source)
    {
        SyntaxNode sourceRoot = CSharpSyntaxTree.ParseText(source).GetRoot();
        OutputKind outputKind = sourceRoot.ChildNodes().OfType<GlobalStatementSyntax>().Any() ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary;

        Project project = workspace.AddProject(name: "CodeFixTests", LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp14))
            .WithCompilationOptions(new CSharpCompilationOptions(outputKind, allowUnsafe: true))
            .AddMetadataReferences(AnalyzerTestHarness.References);

        Assert.True(workspace.TryApplyChanges(project.Solution));

        return workspace.AddDocument(project.Id, name: "ExampleType.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static async Task<string> Fix(string source, DiagnosticAnalyzer analyzer, CodeFixProvider provider, bool fixAll)
    {
        using AdhocWorkspace workspace = new();

        Document document = CreateDocument(workspace, source);
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnostics(document, analyzer).ConfigureAwait(continueOnCapturedContext: false);

        Assert.False(diagnostics.IsEmpty);
        List<CodeAction> actions = [];
        await provider.RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostics[index: 0], (action, associatedDiagnostics) => actions.Add(action), CancellationToken.None)
        ).ConfigureAwait(continueOnCapturedContext: false);
        CodeAction action = Assert.Single(actions);

        if (fixAll)
        {
            FixAllContext context = new(
                document,
                provider,
                FixAllScope.Document,
                action.EquivalenceKey,
                [.. provider.FixableDiagnosticIds],
                new CodeFixDiagnosticProvider(analyzer),
                CancellationToken.None
            );

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

        // Only Fix all promises a clean document, so a single fix skips the extra analysis pass.
        if (fixAll)
        {
            ImmutableArray<Diagnostic> remaining = await GetDiagnostics(changedDocument, analyzer).ConfigureAwait(continueOnCapturedContext: false);

            Assert.True(remaining.IsEmpty);
        }

        return (await changedDocument.GetTextAsync().ConfigureAwait(continueOnCapturedContext: false)).ToString();
    }
}
