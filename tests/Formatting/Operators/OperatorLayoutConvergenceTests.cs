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
/// Runs the formatter on statements that broke in the middle of a call, and checks each line breaks at its outermost
/// place instead, whichever rule runs first: at the operators of a statement's value, then at a call's arguments,
/// and only then inside an argument.
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
    /// and a conditional expression splits before its question mark and colon. A call with two arguments wraps them
    /// before a chain inside one breaks, or when its second argument is a string, and stays on one line when nothing
    /// inside has to break; an operator sequence in an argument comes back onto one line while the call around it
    /// wraps.
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
    [InlineData("operators",
        "return string.Equals(file.Trim().ToUpperInvariant().Normalize(), string.Concat(name, original)\n    .Trim()\n    .ToUpperInvariant()\n    .TrimEnd());",
        "return string.Equals(file.Trim().ToUpperInvariant().Normalize(),\n    string.Concat(name, original).Trim().ToUpperInvariant().TrimEnd());")]
    [InlineData("arguments",
        "return string.Equals(file.Trim().ToUpperInvariant().Normalize(), string.Concat(name, original)\n    .Trim()\n    .ToUpperInvariant()\n    .TrimEnd());",
        "return string.Equals(file.Trim().ToUpperInvariant().Normalize(),\n    string.Concat(name, original).Trim().ToUpperInvariant().TrimEnd());")]
    [InlineData("operators",
        "return TryResolveRow(file.Length + \"a text long enough to push the whole call well past the limit of one hundred and twenty-eight characters\".Length,\n    out reference);",
        "return TryResolveRow(file.Length + \"a text long enough to push the whole call well past the limit of one hundred and twenty-eight characters\".Length, out reference);")]
    [InlineData("arguments",
        "return TryResolveRow(file.Length + \"a text long enough to push the whole call well past the limit of one hundred and twenty-eight characters\".Length,\n    out reference);",
        "return TryResolveRow(file.Length + \"a text long enough to push the whole call well past the limit of one hundred and twenty-eight characters\".Length, out reference);")]
    [InlineData("operators",
        "string chosen = string.Concat(file, name, _tableIdsByFile.ContainsKey(file)\n    ? original\n    : \"a fallback long enough that the whole call cannot stay on one line where it stands\");\n\nreturn TryResolveRow(chosen.Length, out reference);",
        "string chosen = string.Concat(file,\n    name,\n    _tableIdsByFile.ContainsKey(file) ? original : \"a fallback long enough that the whole call cannot stay on one line where it stands\");\n\nreturn TryResolveRow(chosen.Length, out reference);")]
    [InlineData("arguments",
        "string chosen = string.Concat(file, name, _tableIdsByFile.ContainsKey(file)\n    ? original\n    : \"a fallback long enough that the whole call cannot stay on one line where it stands\");\n\nreturn TryResolveRow(chosen.Length, out reference);",
        "string chosen = string.Concat(file,\n    name,\n    _tableIdsByFile.ContainsKey(file) ? original : \"a fallback long enough that the whole call cannot stay on one line where it stands\");\n\nreturn TryResolveRow(chosen.Length, out reference);")]
    [InlineData("operators",
        "return TryResolveRow(ResolveTable(file.Length)?.Length\n    ?? \"a fallback long enough to push the argument past the limit of its line all by itself\".Length, out reference);",
        "return TryResolveRow(ResolveTable(file.Length)?.Length ?? \"a fallback long enough to push the argument past the limit of its line all by itself\".Length,\n    out reference);")]
    [InlineData("arguments",
        "return TryResolveRow(ResolveTable(file.Length)?.Length\n    ?? \"a fallback long enough to push the argument past the limit of its line all by itself\".Length, out reference);",
        "return TryResolveRow(ResolveTable(file.Length)?.Length ?? \"a fallback long enough to push the argument past the limit of its line all by itself\".Length,\n    out reference);")]
    [InlineData("operators",
        "string chosen = string.Concat(\"a first text long enough to bring this call close to the limit of the line it stands on\", \"and a second text that takes it well past\");\n\nreturn TryResolveRow(chosen.Length, out reference);",
        "string chosen = string.Concat(\"a first text long enough to bring this call close to the limit of the line it stands on\",\n    \"and a second text that takes it well past\");\n\nreturn TryResolveRow(chosen.Length, out reference);")]
    [InlineData("arguments",
        "string chosen = string.Concat(\"a first text long enough to bring this call close to the limit of the line it stands on\", \"and a second text that takes it well past\");\n\nreturn TryResolveRow(chosen.Length, out reference);",
        "string chosen = string.Concat(\"a first text long enough to bring this call close to the limit of the line it stands on\",\n    \"and a second text that takes it well past\");\n\nreturn TryResolveRow(chosen.Length, out reference);")]
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
