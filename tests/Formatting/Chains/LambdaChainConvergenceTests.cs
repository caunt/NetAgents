using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.CodeFixes.Formatting;
using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Formatting.Chains;
using NetAgents.Analyzers.Formatting.Clauses;
using NetAgents.Analyzers.Formatting.Statements;
using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting.Chains;

/// <summary>
/// Runs the formatter on a chain inside a lambda inside a chain, where each call carries an object initializer or the
/// lambda, and checks the layout rules settle on one shape whichever of them runs first.
/// </summary>
public sealed class LambdaChainConvergenceTests
{
    private const string Method = "using System;\nusing System.Threading.Tasks;\n\nsealed class ClickOptions\n{\n    public int Timeout { get; set; }\n}\n\n"
        + "sealed class Button\n{\n    public Task Click(ClickOptions options)\n    {\n        return Task.CompletedTask;\n    }\n}\n\n"
        + "sealed class Page\n{\n    public Task Watch(Button button, Func<Task> handler)\n    {\n        return handler();\n    }\n}\n\n"
        + "static class Example\n{\n    static async Task Run(Page page, Button closeButton)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies a lambda body left below its arrow joins it, the calls carrying braces go below their receivers, and
    /// every link ends one level deeper than the line its chain starts on, in either order of the rules.
    /// </summary>
    /// <param name="first">The rule that runs first: the one for chains or the one for lambdas.</param>
    [Theory]
    [InlineData("chains")]
    [InlineData("lambdas")]
    public async Task SettlesNestedChains(string first)
    {
        const string written = "await page.Watch(closeButton,\n    async () =>\n    await closeButton.Click(new ClickOptions\n    {\n        Timeout = 10_000\n    })\n"
            + "        .ConfigureAwait(continueOnCapturedContext: false))\n    .ConfigureAwait(continueOnCapturedContext: false);";

        const string expected = "await page\n    .Watch(closeButton,\n        async () => await closeButton\n            .Click(new ClickOptions\n            {\n"
            + "                Timeout = 10_000\n            })\n            .ConfigureAwait(continueOnCapturedContext: false))\n    .ConfigureAwait(continueOnCapturedContext: false);";

        DiagnosticAnalyzer[] analyzers = [new ChainLayoutAnalyzer(), new ArgumentLayoutAnalyzer(), new LambdaBodyPlacementAnalyzer(), new OneLineStatementAnalyzer()];

        CodeFixProvider[] providers =
        [
            new ChainLayoutCodeFixProvider(),
            new ArgumentLayoutCodeFixProvider(),
            new LambdaBodyPlacementCodeFixProvider(),
            new OneLineStatementCodeFixProvider(),
        ];

        if (first is "lambdas")
        {
            Array.Reverse(analyzers);
            Array.Reverse(providers);
        }

        using AdhocWorkspace workspace = new();

        Project project = workspace.AddProject(name: "Nested", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Example.cs", SourceText.From(Wrap(written)), filePath: "Example.cs")
            .Project;

        CodeFixRunner.CodeFixResult run = await CodeFixRunner.Fix(project,
            analyzers: [.. analyzers],
            providers: [.. providers],
            static fault => Assert.Fail(fault),
            CancellationToken.None);

        string formatted = await DocumentText.Read(run.Project.Documents.Single());

        Assert.Equal(Wrap(expected), formatted);
        Assert.True(run.Blocking.IsEmpty);

        foreach (DiagnosticAnalyzer analyzer in analyzers)
            Assert.Empty(await AnalyzerTestHarness.Analyze(analyzer, formatted));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
