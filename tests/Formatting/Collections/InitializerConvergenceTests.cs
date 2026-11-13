using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.CodeFixes.Formatting;
using NetAgents.Analyzers.CodeFixes.StatementGroups;
using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Formatting.Statements;
using NetAgents.Analyzers.StatementGroups;
using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting.Collections;

/// <summary>
/// Runs the formatter on object initializers beside the rules that join statements and argument lists, and checks they
/// settle on the expanded layout after a one-line argument list and after a wrapped one (#55).
/// </summary>
public sealed class InitializerConvergenceTests
{
    // A constructor call whose two long arguments keep its argument list wrapped, one argument per line.
    private const string WrappedList = "return new Order(first + second + third + fourth + first * second * third * fourth + first - second - third - fourth + first / 1,\n    second + third + fourth + first + second * third * fourth * first + second - third - fourth - first + second / 1,\n    third,\n    fourth)";

    private const string Method = "internal sealed record Order(int First, int Second, int Third, int Fourth)\n{\n"
        + "    internal int PromotionReward { get; init; }\n\n    internal int SourceDataId { get; init; }\n\n"
        + "    internal static Order Create(int first, int second, int third, int fourth, int promotion, int source)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies a one-line initializer expands after a one-line and after a wrapped argument list, and an expanded one
    /// stays expanded instead of being joined back onto one line.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the run.</param>
    [Theory]
    [InlineData("return new Order(first, second, third, fourth) { PromotionReward = promotion, SourceDataId = source };",
        "return new Order(first, second, third, fourth)\n{\n    PromotionReward = promotion,\n    SourceDataId = source\n};")]
    [InlineData("return new Order(first, second, third, fourth)\n{\n    PromotionReward = promotion,\n    SourceDataId = source,\n};",
        "return new Order(first, second, third, fourth)\n{\n    PromotionReward = promotion,\n    SourceDataId = source,\n};")]
    [InlineData("WRAPPED { PromotionReward = promotion, SourceDataId = source };",
        "WRAPPED\n{\n    PromotionReward = promotion,\n    SourceDataId = source\n};")]
    public async Task SettlesOnTheExpandedLayout(string written, string expected)
    {
        DiagnosticAnalyzer[] analyzers = [new ArgumentLayoutAnalyzer(), new OneLineStatementAnalyzer(), new StatementGroupingAnalyzer(), new StatementSpacingAnalyzer()];

        CodeFixProvider[] providers =
        [
            new ArgumentLayoutCodeFixProvider(),
            new OneLineStatementCodeFixProvider(),
            new StatementGroupingCodeFixProvider(),
            new StatementSpacingCodeFixProvider(),
        ];

        using AdhocWorkspace workspace = new();

        Project project = workspace.AddProject(name: "Initializers", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Order.cs", SourceText.From(Wrap(written)), filePath: "Order.cs")
            .Project;

        CodeFixRunner.CodeFixResult run = await CodeFixRunner.Fix(project, analyzers: [.. analyzers], providers: [.. providers], static fault => Assert.Fail(fault), CancellationToken.None);
        string formatted = await DocumentText.Read(run.Project.Documents.Single());

        Assert.Equal(Wrap(expected), formatted);
        Assert.True(run.Blocking.IsEmpty);

        foreach (DiagnosticAnalyzer analyzer in analyzers)
            Assert.Empty(await AnalyzerTestHarness.Analyze(analyzer, formatted));
    }

    private static string Wrap(string statements)
    {
        string body = Regex.Replace(statements, pattern: "WRAPPED", WrappedList);

        return Method.Replace(oldValue: "BODY",
            Regex.Replace(body, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
