using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.CodeFixes.Formatting;
using NetAgents.Analyzers.CodeFixes.StatementGroups;
using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Formatting.Chains;
using NetAgents.Analyzers.Formatting.Statements;
using NetAgents.Analyzers.StatementGroups;
using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting.Chains;

/// <summary>
/// Runs the formatter with the chain layout beside the rules that join statements and argument lists, and checks
/// they settle on one layout.
/// </summary>
public sealed class ChainLayoutConvergenceTests
{
    private const string Method = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\nclass Example\n{\n"
        + "    int Run(int[] values, Dictionary<string, int[]> rows)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies a chain of more than four links stays split although its statement would fit on one line, a chain
    /// that fits is joined, and a chain inside an argument list that fits on one line is joined with the list, even
    /// when the line holding the list passes 128 characters.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the run.</param>
    [Theory]
    [InlineData("int total = values.Where(static value => value > 0).Select(static value => value * 2).Distinct().Order().Sum();\nreturn total;",
        "int total = values\n    .Where(static value => value > 0)\n    .Select(static value => value * 2)\n    .Distinct()\n    .Order()\n    .Sum();\n\nreturn total;")]
    [InlineData("int total = values\n    .Where(static value => value > 0)\n    .Sum();\n\nreturn total;",
        "int total = values.Where(static value => value > 0).Sum();\n\nreturn total;")]
    [InlineData("Console.WriteLine(values\n    .Where(static value => value > 0)\n    .Select(static value => value * 2)\n    .Distinct()\n    .Order()\n    .Sum());\n\nreturn 0;",
        "Console.WriteLine(values.Where(static value => value > 0).Select(static value => value * 2).Distinct().Order().Sum());\n\nreturn 0;")]
    [InlineData("int[] lengths = [.. rows.Values.SelectMany(static cells => cells\n    .Where(static cell => cell > 0)\n    .Select(static cell => cell * 2)\n    .ToArray())];\n\nreturn lengths.Length;",
        "int[] lengths = [.. rows.Values.SelectMany(static cells => cells.Where(static cell => cell > 0).Select(static cell => cell * 2).ToArray())];\n\nreturn lengths.Length;")]
    public async Task SettlesWithTheOtherLayoutRules(string written, string expected)
    {
        DiagnosticAnalyzer[] analyzers = [new ChainLayoutAnalyzer(), new OneLineStatementAnalyzer(), new ArgumentLayoutAnalyzer(),
            new StatementGroupingAnalyzer(), new StatementSpacingAnalyzer()];

        CodeFixProvider[] providers = [new ChainLayoutCodeFixProvider(), new OneLineStatementCodeFixProvider(), new ArgumentLayoutCodeFixProvider(),
            new StatementGroupingCodeFixProvider(), new StatementSpacingCodeFixProvider()];

        using AdhocWorkspace workspace = new();

        Project project = workspace
            .AddProject(name: "Chains", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Example.cs", SourceText.From(Wrap(written)), filePath: "Example.cs").Project;

        CodeFixRunner.CodeFixResult run = await CodeFixRunner.Fix(project, analyzers: [.. analyzers], providers: [.. providers], static fault => Assert.Fail(fault), CancellationToken.None);
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
