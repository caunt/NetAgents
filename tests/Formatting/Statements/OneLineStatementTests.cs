using System.Text.RegularExpressions;

using NetAgents.Analyzers.Formatting.Statements;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting.Statements;

/// <summary>
/// Covers simple statements spread over lines although they fit within 128 characters, and the fix that joins them.
/// </summary>
public sealed class OneLineStatementTests
{
    private const string Method = "using System.Linq;\nusing System.Threading.Tasks;\n\npublic sealed class ExampleType\n{\n"
        + "    public async Task<int> Run(Task<int[]> load, int[] values, bool flag)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies statements that fit are joined with the spacing the formatter gives one line: a chain broken before
    /// each dot, parentheses on lines of their own, a split ternary, and a return.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("int[] loaded = await load\n    .ConfigureAwait(continueOnCapturedContext: false);\n\nreturn loaded.Length;",
        "int[] loaded = await load.ConfigureAwait(continueOnCapturedContext: false);\n\nreturn loaded.Length;")]
    [InlineData("int total = (\n    values.Sum()\n)\n    + 1;\n\nreturn total;", "int total = (values.Sum()) + 1;\n\nreturn total;")]
    [InlineData("int count = flag\n    ? values.Length\n    : 0;\n\nreturn count;", "int count = flag ? values.Length : 0;\n\nreturn count;")]
    [InlineData("return values\n    .Where(value => value > 0)\n    .Count();", "return values.Where(value => value > 0).Count();")]
    public async Task JoinsStatementsThatFit(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixOneLineStatements(Wrap(written), fixAll: true));
    }

    /// <summary>
    /// Verifies statements stay as written when they are on one line, would pass 128 characters joined, hold a lambda
    /// with statements, a switch expression, a comment, or a chain of more than four links that NETAGENTS0047 splits,
    /// or contain a string spanning lines.
    /// </summary>
    /// <param name="statements">A method body that must stay exactly as written.</param>
    [Theory]
    [InlineData("int count = values.Length;\n\nreturn count;")]
    [InlineData("string padded = \"A line long enough that joining it with the declaration that holds it passes the one hundred\"\n    + \"and twenty-eight character limit\";\n\nreturn padded.Length;")]
    [InlineData("System.Func<int, int> twice = number =>\n{\n    int doubled = number * 2;\n\n    return doubled;\n};\n\nreturn twice(2);")]
    [InlineData("int size = values.Length switch\n{\n    0 => 0,\n    _ => 1,\n};\n\nreturn size;")]
    [InlineData("int count = values // Every value counts.\n    .Length;\n\nreturn count;")]
    [InlineData("string text = @\"first\nsecond\";\n\nreturn text.Length;")]
    [InlineData("return values\n    .Where(value => value > 0)\n    .Select(value => value * 2)\n    .Distinct()\n    .Order()\n    .Sum();")]
    public async Task KeepsStatementsThatStayWrapped(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new OneLineStatementAnalyzer(), Wrap(statements)));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
