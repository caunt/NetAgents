using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.CodeFixes.Formatting;
using NetAgents.Analyzers.Formatting;

namespace NetAgents.Analyzers.Tests.Infrastructure;

internal static class CodeFixTestHarness
{
    public static async Task<string> FixSpacing(string source, bool fixAll)
    {
        using AdhocWorkspace workspace = new();

        Project project = workspace.AddProject(name: "SpacingTests", LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp14))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References);

        Assert.True(workspace.TryApplyChanges(project.Solution));
        Document document = workspace.AddDocument(project.Id, name: "ExampleType.cs", SourceText.From(source, Encoding.UTF8));
        ImmutableArray<Diagnostic> diagnostics = await GetSpacingDiagnostics(document).ConfigureAwait(continueOnCapturedContext: false);

        Assert.False(diagnostics.IsEmpty);
        StatementSpacingCodeFixProvider provider = new();
        List<CodeAction> actions = [];
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostics[index: 0],
            (action, associatedDiagnostics) => actions.Add(action), CancellationToken.None)).ConfigureAwait(continueOnCapturedContext: false);
        CodeAction action = Assert.Single(actions);

        if (fixAll)
        {
            FixAllContext context = new(document, provider, FixAllScope.Document, action.EquivalenceKey,
                provider.FixableDiagnosticIds.ToArray(), new SpacingDiagnosticProvider(), CancellationToken.None);

            CodeAction? fixAllAction = await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(continueOnCapturedContext: false);
            Assert.NotNull(fixAllAction);
            action = fixAllAction;
        }

        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
        ApplyChangesOperation changes = Assert.Single(operations.OfType<ApplyChangesOperation>());
        Document? changedDocument = changes.ChangedSolution.GetDocument(document.Id);
        Assert.NotNull(changedDocument);

        if (fixAll)
        {
            Assert.True((await GetSpacingDiagnostics(changedDocument).ConfigureAwait(continueOnCapturedContext: false)).IsEmpty);
        }

        return (await changedDocument.GetTextAsync().ConfigureAwait(continueOnCapturedContext: false)).ToString();
    }

    public static async Task<ImmutableArray<Diagnostic>> GetSpacingDiagnostics(Document document)
    {
        Compilation? compilation = await document.Project.GetCompilationAsync().ConfigureAwait(continueOnCapturedContext: false);
        Assert.NotNull(compilation);

        return await compilation.WithAnalyzers([new StatementSpacingAnalyzer()]).GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
