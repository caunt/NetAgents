using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>Checks that cached rejections retain the original correctness checks.</summary>
public sealed class FormattingAnalysisTests
{
    /// <summary>Retains rejections while different accepted optional fixes advance the project.</summary>
    [Fact]
    public async Task CachesRejectionsAcrossFormattingPasses()
    {
        using AdhocWorkspace workspace = new();

        Project project = CreateProject(workspace);
        DependencyAnalyzer analyzer = new(offerFixes: true);
        List<string> messages = [];

        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(
            project,
            [analyzer],
            [new CandidateFix(rejected: true), new CandidateFix(rejected: false)],
            static fault => Assert.Fail(fault),
            CancellationToken.None,
            new(project.Name, messages.Add)
        );

        Assert.True(result.Blocking.IsEmpty);
        Assert.Equal(expected: 4, analyzer.Runs);
        Assert.Equal(expected: 2, messages.Count(static message => message.Contains(value: "unchanged rejected action", StringComparison.Ordinal)));
        Assert.Contains(expectedSubstring: "=> 4;", (await result.Project.Documents.First().GetTextAsync()).ToString(), StringComparison.Ordinal);
    }

    /// <summary>Uses effective compiler diagnostics when the configured analyzers include a suppressor.</summary>
    [Fact]
    public async Task PreservesDiagnosticSuppressorsDuringValidation()
    {
        using AdhocWorkspace workspace = new();

        Project before = CreateProject(workspace)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, generalDiagnosticOption: ReportDiagnostic.Error));

        FormattingAnalysis analysis = new(
            [new UnusedValueSuppressor()],
            static (exception, failed, diagnostic) => Assert.Fail(exception.Message),
            new(before.Name, report: null)
        );

        ImmutableArray<Diagnostic> baseline = await analysis.GetDiagnostics(before, CancellationToken.None);
        Project candidate = Change(before, name: "Reader.cs", source: "internal static class Reader { internal static int Read() { int unused = 0; return 1; } }\n");
        FormattingAnalysis.ValidationResult result = await analysis.Validate(candidate, baseline, actionKey: "unused variable", optional: false, CancellationToken.None);

        Assert.True(result.IntroducedErrors.IsEmpty);
        Assert.DoesNotContain(result.Diagnostics.ToArray(), static diagnostic => diagnostic.Id == "CS0219" && !diagnostic.IsSuppressed);
    }

    /// <summary>An error already present in today's baseline no longer justifies rejecting the action.</summary>
    [Fact]
    public async Task RechecksWhenBaselineAlreadyContainsRejectedError()
    {
        using AdhocWorkspace workspace = new();

        Project before = CreateProject(workspace);
        DependencyAnalyzer analyzer = new();
        FormattingAnalysis analysis = new([analyzer], static (exception, failed, diagnostic) => Assert.Fail(exception.Message), new(before.Name, report: null));
        ImmutableArray<Diagnostic> baseline = await analysis.GetDiagnostics(before, CancellationToken.None);
        Project candidate = Change(before, name: "Reader.cs", source: "internal static class Reader { internal static int Read() => 2; }\n");

        FormattingAnalysis.ValidationResult rejected = await analysis.Validate(candidate, baseline, actionKey: "optional", optional: true, CancellationToken.None);
        FormattingAnalysis.ValidationResult accepted = await analysis.Validate(candidate, rejected.Diagnostics, actionKey: "optional", optional: true, CancellationToken.None);

        Assert.Equal(expected: "PERF0003", Assert.Single(rejected.IntroducedErrors.ToArray()).Id);
        Assert.True(accepted.IntroducedErrors.IsEmpty);
        Assert.Contains(accepted.Diagnostics.ToArray(), static diagnostic => diagnostic.Id == "PERF0003");
        Assert.Equal(expected: 3, analyzer.Runs);
    }

    /// <summary>Reuses an identical rejected candidate, but revalidates after another file changes.</summary>
    [Fact]
    public async Task ReusesOnlyUnchangedRejectedCandidates()
    {
        using AdhocWorkspace workspace = new();

        Project before = CreateProject(workspace);
        DependencyAnalyzer analyzer = new();
        List<string> messages = [];
        FormattingAnalysis analysis = new([analyzer], static (exception, failed, diagnostic) => Assert.Fail(exception.Message), new(before.Name, messages.Add));
        ImmutableArray<Diagnostic> baseline = await analysis.GetDiagnostics(before, CancellationToken.None);
        const string candidateSource = "internal static class Reader { internal static int Read() => 2; }\n";
        Project candidate = Change(before, name: "Reader.cs", candidateSource);

        FormattingAnalysis.ValidationResult first = await analysis.Validate(candidate, baseline, actionKey: "optional", optional: true, CancellationToken.None);
        Project identical = Change(before, name: "Reader.cs", candidateSource);
        FormattingAnalysis.ValidationResult repeated = await analysis.Validate(identical, baseline, actionKey: "optional", optional: true, CancellationToken.None);

        Assert.Equal(expected: "PERF0003", Assert.Single(first.IntroducedErrors.ToArray()).Id);
        Assert.Equal(expected: "PERF0003", Assert.Single(repeated.IntroducedErrors.ToArray()).Id);
        Assert.Equal(expected: 2, analyzer.Runs);
        Assert.Contains(messages, static message => message.Contains(value: "unchanged rejected action", StringComparison.Ordinal));

        Project changed = Change(before, name: "Dependency.cs", source: "internal static class Dependency { internal const int Value = 1; }\n");
        ImmutableArray<Diagnostic> changedBaseline = await analysis.GetDiagnostics(changed, CancellationToken.None);
        Project changedCandidate = Change(changed, name: "Reader.cs", candidateSource);
        FormattingAnalysis.ValidationResult accepted = await analysis.Validate(changedCandidate, changedBaseline, actionKey: "optional", optional: true, CancellationToken.None);

        Assert.True(accepted.IntroducedErrors.IsEmpty);
        Assert.DoesNotContain(accepted.Diagnostics.ToArray(), static diagnostic => diagnostic.Id == "PERF0003");
        Assert.Equal(expected: 4, analyzer.Runs);
    }

    private static Project Change(Project project, string name, string source)
    {
        return project.Documents.Single(document => document.Name == name).WithText(SourceText.From(source)).Project;
    }

    private static Project CreateProject(AdhocWorkspace workspace)
    {
        return workspace.AddProject(name: "Validation", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(
                name: "Reader.cs",
                SourceText.From(text: "internal static class Reader { internal static int Read() => 1; }\n"),
                filePath: "Reader.cs"
            ).Project
            .AddDocument(
                name: "Dependency.cs",
                SourceText.From(text: "internal static class Dependency { internal const int Value = 0; }\n"),
                filePath: "Dependency.cs"
            ).Project;
    }

    private sealed class CandidateFix(bool rejected) : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => rejected ? ["PERF0004"] : ["PERF0005"];

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Change optional candidate",
                    async cancellationToken =>
            {
                SourceText text = await context.Document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                string next = rejected ? "2" : text.ToString(context.Span) == "1" ? "3" : "4";

                return context.Document.WithText(text.WithChanges(new TextChange(context.Span, next)));
            },
                    rejected ? "RejectOptionalCandidate" : "AcceptOptionalCandidate"
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }

    [SuppressMessage(
        "MicrosoftCodeAnalysisCorrectness",
        "RS1001",
        Justification = "This private test analyzer is constructed explicitly and must not be discovered by compiler hosts."
    )]
    [SuppressMessage(
        "MicrosoftCodeAnalysisReleaseTracking",
        "RS2008",
        Justification = "This private instrumentation rule is only used in tests and is not shipped."
    )]
    private sealed class DependencyAnalyzer(bool offerFixes = false) : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RejectedSuggestion = new(
            id: "PERF0004",
            title: "Rejected candidate",
            messageFormat: "Rejected candidate",
            category: "Testing",
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            customTags: [WellKnownDiagnosticTags.CompilationEnd]
        );

        private static readonly DiagnosticDescriptor AcceptedSuggestion = new(
            id: "PERF0005",
            title: "Accepted candidate",
            messageFormat: "Accepted candidate",
            category: "Testing",
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            customTags: [WellKnownDiagnosticTags.CompilationEnd]
        );

        private static readonly DiagnosticDescriptor Rule = new(
            id: "PERF0003",
            title: "Dependent error",
            messageFormat: "Dependent error",
            category: "Testing",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            customTags: [WellKnownDiagnosticTags.CompilationEnd]
        );

        public int Runs { get; private set; }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule, RejectedSuggestion, AcceptedSuggestion];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationAction(
                compilation =>
            {
                Runs++;
                SyntaxTree reader = compilation.Compilation.SyntaxTrees.Single(static tree => tree.FilePath == "Reader.cs");
                SyntaxTree dependency = compilation.Compilation.SyntaxTrees.Single(static tree => tree.FilePath == "Dependency.cs");
                string source = reader.GetText().ToString();
                string dependencySource = dependency.GetText().ToString();

                bool invalid = source.Contains(value: "=> 2;", StringComparison.Ordinal) && dependencySource.Contains(value: "= 0;", StringComparison.Ordinal);

                if (invalid)
                    compilation.ReportDiagnostic(Diagnostic.Create(Rule, reader.GetRoot().GetFirstToken().GetLocation()));

                if (offerFixes)
                {
                    SyntaxToken token = reader.GetRoot().DescendantTokens().Single(static token => token.IsKind(SyntaxKind.NumericLiteralToken));
                    compilation.ReportDiagnostic(Diagnostic.Create(RejectedSuggestion, token.GetLocation()));

                    if (token.ValueText != "4")
                        compilation.ReportDiagnostic(Diagnostic.Create(AcceptedSuggestion, token.GetLocation()));
                }
            }
            );
        }
    }

    [SuppressMessage(
        "MicrosoftCodeAnalysisCorrectness",
        "RS1001",
        Justification = "This private test suppressor is constructed explicitly and is not shipped."
    )]
    private sealed class UnusedValueSuppressor : DiagnosticSuppressor
    {
        private static readonly SuppressionDescriptor Descriptor = new(id: "PERFSP0001", suppressedDiagnosticId: "CS0219", justification: "Exercise effective compiler diagnostics.");

        public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => [Descriptor];

        public override void ReportSuppressions(SuppressionAnalysisContext context)
        {
            foreach (Diagnostic diagnostic in context.ReportedDiagnostics.Where(static diagnostic => diagnostic.Id == "CS0219"))
                context.ReportSuppression(Suppression.Create(Descriptor, diagnostic));
        }
    }
}
