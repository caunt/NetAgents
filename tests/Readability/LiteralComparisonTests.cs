using System.Text.RegularExpressions;

using NetAgents.Analyzers.Readability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

/// <summary>
/// Covers string.Equals calls against a string literal, and the fix that writes them as constant patterns.
/// </summary>
public sealed class LiteralComparisonTests
{
    private const string Method = "using System;\nusing System.Linq;\nusing System.Linq.Expressions;\n\npublic static class ExampleType\n{\n"
        + "    public static bool Run(string value, string other)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies calls stay as written when they compare without ordinal rules, compare two variables, go through the
    /// instance method, or sit in an expression tree or a query expression, where patterns cannot stand.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("return string.Equals(value, \"x\", StringComparison.OrdinalIgnoreCase);")]
    [InlineData("return string.Equals(value, other, StringComparison.Ordinal);")]
    [InlineData("return value.Equals(\"x\", StringComparison.Ordinal);")]
    [InlineData("Expression<Func<string, bool>> test = text => string.Equals(text, \"x\");\n\nreturn test.Compile()(value);")]
    [InlineData("return (from text in new[] { value } where string.Equals(text, \"x\") select text).Any();")]
    public async Task KeepsOtherComparisons(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new LiteralComparisonAnalyzer(), Wrap(statements)));
    }

    /// <summary>
    /// Verifies the fix writes a pattern for either argument order, with or without the ordinal comparison, turns a
    /// negation into is not, and adds parentheses where an operator around or inside would bind differently.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("return string.Equals(value, \"--help\", StringComparison.Ordinal);", "return value is \"--help\";")]
    [InlineData("return string.Equals(\"--help\", value);", "return value is \"--help\";")]
    [InlineData("return !string.Equals(value, b: \"--help\", StringComparison.Ordinal);", "return value is not \"--help\";")]
    [InlineData("return string.Equals(value, \"-h\", StringComparison.Ordinal) || string.Equals(value, \"--help\", StringComparison.Ordinal);",
        "return value is \"-h\" || value is \"--help\";")]
    [InlineData("return string.Equals(value, \"x\") == string.Equals(other, \"y\");", "return (value is \"x\") == (other is \"y\");")]
    [InlineData("return string.Equals(value ?? other, \"x\");", "return (value ?? other) is \"x\";")]
    public async Task WritesConstantPatterns(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixLiteralComparisons(Wrap(written), fixAll: true));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
