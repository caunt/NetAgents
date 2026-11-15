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
using NetAgents.Analyzers.Formatting.Operators;
using NetAgents.Analyzers.Formatting.Statements;
using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting.Operators;

/// <summary>
/// Runs the formatter on a call whose arguments were wrapped in the middle of a logical sequence or a conditional
/// expression, and checks the line breaks at the operators instead, whichever rule runs first.
/// </summary>
public sealed class OperatorLayoutConvergenceTests
{
    private const string Method = "using System.Collections.Generic;\n\nsealed class Example\n{\n    private readonly Dictionary<string, int> _tableIdsByFile = [];\n\n"
        + "    public bool TryResolve(string file, string name, string original, out int reference)\n    {\n        reference = 0;\n\nBODY\n    }\n\n"
        + "    private static string? ResolveTable(int tableId)\n    {\n        return tableId > 0 ? \"table\" : null;\n    }\n\n"
        + "    private static bool TryFindRow(string file, string table, string name, out int rowIndex)\n    {\n        rowIndex = file.Length + table.Length + name.Length;\n\n        return true;\n    }\n\n"
        + "    private static bool TryResolveRow(int globalId, out int reference)\n    {\n        reference = globalId;\n\n        return true;\n    }\n}\n";

    /// <summary>
    /// Verifies the sequence splits before each operator and the call inside it gets its arguments back on one line,
    /// and a conditional expression splits before its question mark and colon.
    /// </summary>
    /// <param name="first">The rule that runs first: the one for operators or the one for arguments.</param>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the run.</param>
    [Theory]
    [InlineData("operators",
        "return _tableIdsByFile.TryGetValue(file, out int tableId) && ResolveTable(tableId) is { } table && TryFindRow(file,\n    table,\n    name,\n    out int rowIndex)\n    && TryResolveRow((tableId * 1000) + rowIndex, out reference);",
        "return _tableIdsByFile.TryGetValue(file, out int tableId)\n    && ResolveTable(tableId) is { } table\n    && TryFindRow(file, table, name, out int rowIndex)\n    && TryResolveRow((tableId * 1000) + rowIndex, out reference);")]
    [InlineData("arguments",
        "return _tableIdsByFile.TryGetValue(file, out int tableId) && ResolveTable(tableId) is { } table && TryFindRow(file,\n    table,\n    name,\n    out int rowIndex)\n    && TryResolveRow((tableId * 1000) + rowIndex, out reference);",
        "return _tableIdsByFile.TryGetValue(file, out int tableId)\n    && ResolveTable(tableId) is { } table\n    && TryFindRow(file, table, name, out int rowIndex)\n    && TryResolveRow((tableId * 1000) + rowIndex, out reference);")]
    [InlineData("operators",
        "string chosen = _tableIdsByFile.ContainsKey(file) ? original : string.Concat(file,\n    name,\n    \"a suffix long enough that the whole expression cannot stay on one line\");\n\nreturn TryResolveRow(chosen.Length, out reference);",
        "string chosen = _tableIdsByFile.ContainsKey(file)\n    ? original\n    : string.Concat(file, name, \"a suffix long enough that the whole expression cannot stay on one line\");\n\nreturn TryResolveRow(chosen.Length, out reference);")]
    [InlineData("arguments",
        "string chosen = _tableIdsByFile.ContainsKey(file) ? original : string.Concat(file,\n    name,\n    \"a suffix long enough that the whole expression cannot stay on one line\");\n\nreturn TryResolveRow(chosen.Length, out reference);",
        "string chosen = _tableIdsByFile.ContainsKey(file)\n    ? original\n    : string.Concat(file, name, \"a suffix long enough that the whole expression cannot stay on one line\");\n\nreturn TryResolveRow(chosen.Length, out reference);")]
    public async Task BreaksAtOperatorsBeforeArguments(string first, string written, string expected)
    {
        DiagnosticAnalyzer[] analyzers =
        [
            new OperatorLayoutAnalyzer(),
            new ChainLayoutAnalyzer(),
            new ValuePlacementAnalyzer(),
            new OneLineStatementAnalyzer(),
            new ArgumentLayoutAnalyzer(),
        ];

        CodeFixProvider[] providers =
        [
            new OperatorLayoutCodeFixProvider(),
            new ChainLayoutCodeFixProvider(),
            new ValuePlacementCodeFixProvider(),
            new OneLineStatementCodeFixProvider(),
            new ArgumentLayoutCodeFixProvider(),
        ];

        if (first is "arguments")
        {
            Array.Reverse(analyzers);
            Array.Reverse(providers);
        }

        using AdhocWorkspace workspace = new();

        Project project = workspace.AddProject(name: "Operators", LanguageNames.CSharp)
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
