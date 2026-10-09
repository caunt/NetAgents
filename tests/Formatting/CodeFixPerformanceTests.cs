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

/// <summary>Checks the work needed to format accepted and rejected project states.</summary>
public sealed class CodeFixPerformanceTests
{
    /// <summary>Validates each accepted state once, with required repairs preceding optional actions.</summary>
    /// <param name="suggestions">Whether to offer an unrelated compiler-breaking suggestion.</param>
    [Theory]
    [InlineData("none")]
    [InlineData("optional")]
    public async Task AnalyzesAcceptedStatesOnce(string suggestions)
    {
        using AdhocWorkspace workspace = new();

        bool includeSuggestion = suggestions == "optional";
        Project project = CreateProject(workspace);
        CountingAnalyzer analyzer = new(includeSuggestion);
        List<string> actions = [];
        List<string> messages = [];
        StepFix required = new(actions);
        SuggestionFix optional = new(actions);
        FormattingProgress progress = new(project.Name, messages.Add);

        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(project, [analyzer], [optional, required], static fault => Assert.Fail(fault), CancellationToken.None, progress);

        Assert.True(result.Blocking.IsEmpty);
        Assert.Equal(expected: 4, analyzer.Runs);
        Assert.Equal(expected: 8, analyzer.TreeVisits);
        Assert.Equal(includeSuggestion ? ["required", "required", "required", "optional"] : ["required", "required", "required"], actions);
        Assert.Equal(expected: 4, analyzer.States.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(expectedSubstring: "=> 3;", (await result.Project.Documents.First().GetTextAsync()).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "static  int", (await result.Project.Documents.First().GetTextAsync()).ToString(), StringComparison.Ordinal);
        Assert.Contains(messages, static message => message.Contains(value: "pass 1, elapsed", StringComparison.Ordinal));
        Assert.Contains(messages, static message => message.Contains(value: "pass 4, elapsed", StringComparison.Ordinal));
        Assert.Contains(messages, static message => message.Contains(value: "validating PERF0002 action", StringComparison.Ordinal));
        Assert.Contains(expectedSubstring: "finished applying available fixes", messages.Last(), StringComparison.Ordinal);

        if (includeSuggestion)
            Assert.Contains(messages, static message => message.Contains(value: "rejected PERF0001 action", StringComparison.Ordinal));
    }

    /// <summary>Reports the malformed switch before formatting or invoking any analyzer or fix.</summary>
    /// <param name="fileName">An authored or generated source containing the parse error.</param>
    [Theory]
    [InlineData("Step.cs")]
    [InlineData("Step.g.cs")]
    public async Task ReportsSyntaxErrorsBeforeStyleWork(string fileName)
    {
        using AdhocWorkspace workspace = new();

        const string source = """
            class Step
            {
                void Run(int command)
                {
                    switch (command)
                    {
                        case 0:
                            {
                                break;
                        case 1:
                                break;
                            }
                    }
                }
            }
            """;

        Project project = workspace.AddProject(name: "Malformed", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(fileName, SourceText.From(source), filePath: fileName).Project;

        CountingAnalyzer analyzer = new(includeSuggestion: true);
        List<string> actions = [];
        List<string> messages = [];

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CodeFixRunner.Fix(
                project,
                [analyzer],
                [new StepFix(actions)],
                static fault => Assert.Fail(fault),
                CancellationToken.None,
                new(project.Name, messages.Add)
            )
        );

        Assert.Contains(expectedSubstring: "source contains syntax errors", exception.Message, StringComparison.Ordinal);
        Assert.Contains(fileName + "(", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "CS1513", exception.Message, StringComparison.Ordinal);
        Assert.Equal(expected: 0, analyzer.Runs);
        Assert.Equal(expected: 0, analyzer.TreeVisits);
        Assert.Empty(actions);
        Assert.Contains(expectedSubstring: "checking source syntax", Assert.Single(messages), StringComparison.Ordinal);
        Assert.Equal(source, (await project.Documents.Single().GetTextAsync()).ToString());
    }

    /// <summary>Cannot accept a fix that changes the project settings used to validate the source.</summary>
    [Fact]
    public async Task SkipsProjectSettingChanges()
    {
        using AdhocWorkspace workspace = new();

        Project project = CreateProject(workspace);
        CountingAnalyzer analyzer = new(includeSuggestion: false);
        List<string> actions = [];
        StepFix valid = new(actions);

        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(project, [analyzer], [new ProjectSettingsFix(), valid], static fault => Assert.Fail(fault), CancellationToken.None);

        Assert.True(result.Blocking.IsEmpty);
        Assert.Equal(project.CompilationOptions, result.Project.CompilationOptions);
        Assert.Equal(expected: 3, actions.Count);
        Assert.Equal(expected: 4, analyzer.Runs);
    }

    private static Project CreateProject(AdhocWorkspace workspace)
    {
        return workspace.AddProject(name: "Performance", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Step.cs", SourceText.From(text: "internal static class Step { internal static int Read() => 0; }\n"), filePath: "Step.cs").Project
            .AddDocument(
                name: "Suggestion.cs",
                SourceText.From(text: "internal static class Suggestion { internal static int Read() => 1; }\n"),
                filePath: "Suggestion.cs"
            ).Project;
    }

    [SuppressMessage(
        "MicrosoftCodeAnalysisCorrectness",
        "RS1001",
        Justification = "This private test analyzer is constructed explicitly and must not be discovered by compiler hosts."
    )]
    [SuppressMessage(
        "MicrosoftCodeAnalysisReleaseTracking",
        "RS2008",
        Justification = "These private instrumentation rules are only used in tests and are not shipped."
    )]
    private sealed class CountingAnalyzer(bool includeSuggestion) : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RequiredRule = new(
            id: "PERF0002",
            title: "Required repair",
            messageFormat: "Required repair",
            category: "Testing",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            customTags: [WellKnownDiagnosticTags.CompilationEnd]
        );

        private static readonly DiagnosticDescriptor SuggestionRule = new(
            id: "PERF0001",
            title: "Optional repair",
            messageFormat: "Optional repair",
            category: "Testing",
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            customTags: [WellKnownDiagnosticTags.CompilationEnd]
        );

        public int Runs { get; private set; }

        public List<string> States { get; } = [];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [RequiredRule, SuggestionRule];

        public int TreeVisits { get; private set; }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationAction(
                compilation =>
            {
                Runs++;
                SyntaxTree[] trees = [.. compilation.Compilation.SyntaxTrees];
                TreeVisits += trees.Length;
                States.Add(string.Join(separator: "\n", trees.Select(static tree => tree.GetText().ToString())));

                SyntaxTree step = trees.Single(static tree => tree.FilePath == "Step.cs");
                SyntaxToken token = step.GetRoot().DescendantTokens().Single(static token => token.IsKind(SyntaxKind.NumericLiteralToken));

                if (token.ValueText != "3")
                    compilation.ReportDiagnostic(Diagnostic.Create(RequiredRule, token.GetLocation()));

                if (includeSuggestion)
                {
                    SyntaxTree suggestion = trees.Single(static tree => tree.FilePath == "Suggestion.cs");
                    compilation.ReportDiagnostic(Diagnostic.Create(SuggestionRule, suggestion.GetRoot().GetFirstToken().GetLocation()));
                }
            }
            );
        }
    }

    private sealed class ProjectSettingsFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ["PERF0002"];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Change compilation settings",
                    async cancellationToken =>
            {
                SourceText text = await context.Document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                Document changed = context.Document.WithText(text.WithChanges(new TextChange(context.Span, newText: "3")));

                return changed.Project.WithCompilationOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication)).Solution;
            }
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }

    private sealed class StepFix(List<string> actions) : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ["PERF0002"];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Advance required repair",
                    async cancellationToken =>
            {
                actions.Add(item: "required");
                SourceText text = await context.Document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                string next = text.ToString(context.Span) switch { "0" => "1", "1" => "2", _ => "3" };
                string changed = text.WithChanges(new TextChange(context.Span, next)).ToString();

                return context.Document.WithText(SourceText.From(changed.Replace(oldValue: "static int", newValue: "static  int", StringComparison.Ordinal)));
            }
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }

    private sealed class SuggestionFix(List<string> actions) : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ["PERF0001"];

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Break optional return",
                    async cancellationToken =>
            {
                actions.Add(item: "optional");
                SourceText text = await context.Document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                return context.Document.WithText(SourceText.From(text.ToString().Replace(oldValue: "=> 1;", newValue: "=> \"text\";", StringComparison.Ordinal)));
            },
                    equivalenceKey: "BreakOptionalReturn"
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }
}
