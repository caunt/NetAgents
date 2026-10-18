using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.BuildTasks;

// The documents the formatter may rewrite are the ones the analyzers read. Asking the analyzer driver
// keeps that true for every rule Roslyn applies: generated file names, a leading auto-generated comment,
// and generated_code in configuration. The copy of those rules this replaced also matched the marker
// inside a string literal, so a file the analyzers still reported on was never fixed (#39).
internal static class EditableDocuments
{
    public static async Task<ImmutableHashSet<DocumentId>> Find(Project project, CancellationToken cancellationToken)
    {
        Compilation compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
            ?? throw new InvalidOperationException(message: "The formatting compilation is unavailable.");

        CompilationWithAnalyzersOptions options = new(project.AnalyzerOptions, onAnalyzerException: null, concurrentAnalysis: false, logAnalyzerExecutionTime: false);
        CompilationWithAnalyzers analysis = compilation.WithAnalyzers([new AuthoredTreeAnalyzer()], options);
        List<DocumentId> editable = [];

        foreach (Document document in project.Documents)
        {
            SyntaxTree? tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (tree is null)
                continue;

            ImmutableArray<Diagnostic> marks = await analysis.GetAnalyzerSyntaxDiagnosticsAsync(tree, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (!marks.IsEmpty)
                editable.Add(document.Id);
        }

        return [.. editable];
    }

    // Skipping generated code the way policy analyzers do, it marks each tree the driver hands it, so the
    // marks name exactly the trees an analyzer reads. The rule cannot be configured away, because a tree
    // left unmarked would be one the formatter stops formatting.
    [SuppressMessage(
        "MicrosoftCodeAnalysisCorrectness",
        "RS1001",
        Justification = "The formatter constructs this analyzer itself, and no compiler host may discover it."
    )]
    [SuppressMessage("MicrosoftCodeAnalysisReleaseTracking", "RS2008", Justification = "The formatter's private marker is never shipped as a rule.")]
    private sealed class AuthoredTreeAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Mark = new(
            id: "NETAGENTSFORMAT",
            title: "Authored source",
            messageFormat: "The analyzers read this file",
            category: "NetAgents.Formatter",
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true,
            customTags: [WellKnownDiagnosticTags.NotConfigurable]
        );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Mark];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxTreeAction(static tree => tree.ReportDiagnostic(Diagnostic.Create(Mark, Location.Create(tree.Tree, textSpan: default))));
        }
    }
}
