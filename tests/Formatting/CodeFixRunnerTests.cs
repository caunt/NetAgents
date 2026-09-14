using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>Checks formatter progress with unresolved compiler diagnostics.</summary>
public sealed class CodeFixRunnerTests
{

    /// <summary>Still applies valid automatic fixes to compiler errors.</summary>
    [Theory]
    [InlineData("valid")]
    [InlineData("noop")]
    public async Task PreservesValidCompilerFix(string firstAction)
    {
        using AdhocWorkspace workspace = new();

        CompilerFix provider = new(mode: "valid");
        CodeFixProvider[] providers = firstAction == "noop" ? [new CompilerFix(mode: "noop"), provider] : [provider];
        Project result = await CodeFixRunner.Fix(CreateProject(workspace), [new MethodSpacingAnalyzer()], providers, CancellationToken.None);
        Compilation? compilation = await result.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation.GetDiagnostics().ToArray(), static diagnostic => diagnostic.Id == "CS0122");
        Assert.Equal(expected: 1, provider.Attempts);
    }

    /// <summary>Reports stalled actions without exhausting the pass limit.</summary>
    [Theory]
    [InlineData("noop")]
    [InlineData("whitespace")]
    [InlineData("cycle")]
    public async Task ReportsStalledCompilerFix(string mode)
    {
        using AdhocWorkspace workspace = new();

        Project project = CreateProject(workspace);
        CompilerFix provider = new(mode);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CodeFixRunner.Fix(project, [new MethodSpacingAnalyzer()], [provider], CancellationToken.None));

        Assert.Contains(expectedSubstring: "CS0122", exception.Message, StringComparison.Ordinal);
        Assert.Matches(expectedRegexPattern: @"Example\.cs\([1-9][0-9]*,[1-9][0-9]*\): error CS0122", exception.Message);
        Assert.Contains(expectedSubstring: "Attempt accessibility repair", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "64 fix passes", exception.Message, StringComparison.Ordinal);
        Assert.InRange(provider.Attempts, low: 1, high: 3);
    }

    private static Project CreateProject(AdhocWorkspace workspace)
    {
        return workspace.AddProject(name: "Progress", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(
                name: "Example.cs",
                SourceText.From(text: "class Hidden { private static int Value; } class Consumer { int Read() => Hidden.Value; }"),
                filePath: "Example.cs"
            ).Project;
    }

    private sealed class CompilerFix(string mode) : CodeFixProvider
    {
        public int Attempts { get; private set; }

        public override ImmutableArray<string> FixableDiagnosticIds => ["CS0122"];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            Attempts++;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Attempt accessibility repair",
                    async cancellationToken =>
            {
                SourceText text = await context.Document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                string source = text.ToString();

                string changed = mode switch
                {
                    "valid" => source.Replace(oldValue: "private", newValue: "public", StringComparison.Ordinal),
                    "whitespace" => source.Replace(oldValue: "class Hidden", newValue: "class  Hidden", StringComparison.Ordinal),
                    "cycle" => source.Contains(value: "class Consumer", StringComparison.Ordinal)
                        ? source.Replace(oldValue: "class Consumer", newValue: "class Alternate", StringComparison.Ordinal)
                        : source.Replace(oldValue: "class Alternate", newValue: "class Consumer", StringComparison.Ordinal),
                    _ => source,
                };

                return context.Document.WithText(SourceText.From(changed));
            }
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }
}
