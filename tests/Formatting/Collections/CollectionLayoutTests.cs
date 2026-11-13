using System.Text.RegularExpressions;

using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting.Collections;

/// <summary>
/// Covers the layout NETAGENTS0020 gives collection expressions and initializers: object-like initializers always one
/// element per line, and collections of two or more elements one line when they fit.
/// </summary>
public sealed class CollectionLayoutTests
{
    private const string Method = "using System;\nusing System.Collections.Generic;\n\npublic sealed class ExampleType\n{\n"
        + "    public int First { get; init; }\n\n    public int Second { get; init; }\n\n    public void Run()\n    {\nBODY\n    }\n}\n\n"
        + "public sealed record Pair(int Left, int Right);\n";

    /// <summary>
    /// Verifies collections stay as written when they hold one element, hold a comment, or already have the layout:
    /// one line when they fit, otherwise one element per line, and object initializers expanded with or without a
    /// trailing comma.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("int[] values = [1];\n\nConsole.WriteLine(values.Length);")]
    [InlineData("int[] values = [1, // The first value.\n    2];\n\nConsole.WriteLine(values.Length);")]
    [InlineData("List<int> values = new() { 1, 2 };\n\nConsole.WriteLine(values.Count);")]
    [InlineData("ExampleType example = new()\n{\n    First = 1,\n};\n\nConsole.WriteLine(example.First);")]
    [InlineData("ExampleType example = new()\n{\n    First = 1,\n    Second = 2\n};\n\nConsole.WriteLine(example.First);")]
    [InlineData("string[] lines =\n[\n    \"First line of the help text that is long enough\",\n    \"Second line of the help text that is long enough\",\n    \"Third line of the help text\",\n];\n\nConsole.WriteLine(lines.Length);")]
    public async Task KeepsSettledCollections(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), Wrap(statements)));
    }

    /// <summary>
    /// Verifies the fix joins a collection expression that fits onto the line before it without its trailing comma, gives
    /// one that does not fit its brackets on lines of their own and one element per line, each followed by a comma, and
    /// expands object initializers, with expressions, anonymous objects, and array rows however short they are.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("int[] values =\n[\n    1,\n    2,\n];\n\nConsole.WriteLine(values.Length);",
        "int[] values = [1, 2];\n\nConsole.WriteLine(values.Length);")]
    [InlineData("ExampleType example = new() { First = 1, Second = 2 };\n\nConsole.WriteLine(example.First);",
        "ExampleType example = new()\n{\n    First = 1,\n    Second = 2\n};\n\nConsole.WriteLine(example.First);")]
    [InlineData("ExampleType example = new() { First = 1 };\n\nConsole.WriteLine(example.First);",
        "ExampleType example = new()\n{\n    First = 1\n};\n\nConsole.WriteLine(example.First);")]
    [InlineData("Pair copy = new Pair(Left: 1, Right: 2) with { Left = 3 };\n\nConsole.WriteLine(copy.Left);",
        "Pair copy = new Pair(Left: 1, Right: 2) with\n{\n    Left = 3\n};\n\nConsole.WriteLine(copy.Left);")]
    [InlineData("var item = new { Name = \"first\", Count = 1 };\n\nConsole.WriteLine(item.Count);",
        "var item = new\n{\n    Name = \"first\",\n    Count = 1\n};\n\nConsole.WriteLine(item.Count);")]
    [InlineData("int[,] grid = new int[,] { { 1, 2 }, { 3, 4 } };\n\nConsole.WriteLine(grid.Length);",
        "int[,] grid = new int[,]\n{\n    {\n        1,\n        2\n    },\n    {\n        3,\n        4\n    }\n};\n\nConsole.WriteLine(grid.Length);")]
    [InlineData("string[] lines = [\"First line of the help text that is long enough\",\n    \"Second line of the help text that is long enough\",\n    \"Third line of the help text\"];\n\nConsole.WriteLine(lines.Length);",
        "string[] lines =\n[\n    \"First line of the help text that is long enough\",\n    \"Second line of the help text that is long enough\",\n    \"Third line of the help text\",\n];\n\nConsole.WriteLine(lines.Length);")]
    public async Task LaysOutCollections(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixArguments(Wrap(written), fixAll: true));
    }

    /// <summary>
    /// Verifies an initializer that does not fit gets every element on a line of its own and is reported until it does.
    /// </summary>
    [Fact]
    public async Task SplitsLongInitializers()
    {
        const string written = "Dictionary<string, string> names = new() { [\"first\"] = \"A long value that makes this initializer longer\", "
            + "[\"second\"] = \"Another long value that pushes it well past the limit\" };\n\nConsole.WriteLine(names.Count);";

        string formatted = await CodeFixTestHarness.FixArguments(Wrap(written), fixAll: true);

        Assert.Contains(expectedSubstring: "\n            [\"first\"] = \"A long value", formatted, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "\n            [\"second\"] = \"Another long value", formatted, StringComparison.Ordinal);
        Assert.NotEmpty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), Wrap(written)));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
