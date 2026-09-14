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
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CodeFixRunner.Fix(project, [new MethodSpacingAnalyzer()], [provider], CancellationToken.None));

        Assert.Contains("CS0122", exception.Message, StringComparison.Ordinal);
        Assert.Matches(@"Example\.cs\([1-9][0-9]*,[1-9][0-9]*\): error CS0122", exception.Message);
        Assert.Contains("Attempt accessibility repair", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("64 fix passes", exception.Message, StringComparison.Ordinal);
        Assert.InRange(provider.Attempts, 1, 3);
    }

    /// <summary>Still applies valid automatic fixes to compiler errors.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreservesValidCompilerFix(bool tryNoopFirst)
    {
        using AdhocWorkspace workspace = new();
        CompilerFix provider = new("valid");
        CodeFixProvider[] providers = tryNoopFirst ? [new CompilerFix("noop"), provider] : [provider];
        Project result = await CodeFixRunner.Fix(CreateProject(workspace), [new MethodSpacingAnalyzer()], providers, CancellationToken.None);
        Compilation? compilation = await result.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0122");
        Assert.Equal(1, provider.Attempts);
    }

    private static Project CreateProject(AdhocWorkspace workspace)
    {
        return workspace.AddProject("Progress", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument("Example.cs", SourceText.From("class Hidden { private static int Value; } class Consumer { int Read() => Hidden.Value; }"), filePath: "Example.cs").Project;
    }

    private sealed class CompilerFix(string mode) : CodeFixProvider
    {
        public int Attempts { get; private set; }

        public override ImmutableArray<string> FixableDiagnosticIds => ["CS0122"];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            Attempts++;
            context.RegisterCodeFix(CodeAction.Create("Attempt accessibility repair", async cancellationToken =>
            {
                SourceText text = await context.Document.GetTextAsync(cancellationToken);
                string source = text.ToString();
                string changed = mode switch
                {
                    "valid" => source.Replace("private", "public", StringComparison.Ordinal),
                    "whitespace" => source.Replace("class Hidden", "class  Hidden", StringComparison.Ordinal),
                    "cycle" => source.Contains("class Consumer", StringComparison.Ordinal)
                        ? source.Replace("class Consumer", "class Alternate", StringComparison.Ordinal)
                        : source.Replace("class Alternate", "class Consumer", StringComparison.Ordinal),
                    _ => source,
                };

                return context.Document.WithText(SourceText.From(changed));
            }), context.Diagnostics);
            return Task.CompletedTask;
        }
    }
}
