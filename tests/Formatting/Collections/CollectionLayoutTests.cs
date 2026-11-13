using System.Text.RegularExpressions;

using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting.Collections;

/// <summary>
/// Covers the layout NETAGENTS0020 gives collection expressions and initializers of two or more elements.
/// </summary>
public sealed class CollectionLayoutTests
{
    private const string Method = "using System;\nusing System.Collections.Generic;\n\npublic sealed class ExampleType\n{\n"
        + "    public int First { get; init; }\n\n    public int Second { get; init; }\n\n    public void Run()\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies collections stay as written when they hold one element, hold a comment, or already have the layout:
    /// one line when they fit, otherwise one element per line.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("int[] values = [1];\n\nConsole.WriteLine(values.Length);")]
    [InlineData("int[] values = [1, // The first value.\n    2];\n\nConsole.WriteLine(values.Length);")]
    [InlineData("List<int> values = new() { 1, 2 };\n\nConsole.WriteLine(values.Count);")]
    [InlineData("string[] lines =\n[\n    \"First line of the help text that is long enough\",\n    \"Second line of the help text that is long enough\",\n    \"Third line of the help text\",\n];\n\nConsole.WriteLine(lines.Length);")]
    public async Task KeepsSettledCollections(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), Wrap(statements)));
    }

    /// <summary>
    /// Verifies the fix joins a collection expression and an initializer that fit onto the line before them without
    /// their trailing comma, and gives a collection expression that does not fit one element per line, its first beside
    /// the bracket and none after the last.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("int[] values =\n[\n    1,\n    2,\n];\n\nConsole.WriteLine(values.Length);",
        "int[] values = [1, 2];\n\nConsole.WriteLine(values.Length);")]
    [InlineData("ExampleType example = new()\n{\n    First = 1,\n    Second = 2,\n};\n\nConsole.WriteLine(example.First);",
        "ExampleType example = new() { First = 1, Second = 2 };\n\nConsole.WriteLine(example.First);")]
    [InlineData("string[] lines =\n[\n    \"First line of the help text that is long enough\",\n    \"Second line of the help text that is long enough\",\n    \"Third line of the help text\",\n];\n\nConsole.WriteLine(lines.Length);",
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
