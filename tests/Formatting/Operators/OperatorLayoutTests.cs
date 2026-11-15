using System.Text.RegularExpressions;

using NetAgents.Analyzers.Formatting.Operators;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting.Operators;

/// <summary>
/// Covers the layout NETAGENTS0055 gives logical and coalescing sequences and conditional expressions, the ones it
/// leaves as written, and the fix.
/// </summary>
public sealed class OperatorLayoutTests
{
    private const string Method = "using System;\n\npublic static class ExampleType\n{\n    public static bool Run(bool first, bool second, string name, string fallback)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies expressions stay as written when they fit on one line, are already split with their operators in
    /// front, sit in a condition, which NETAGENTS0019 keeps on one line, have a comment beside an operator, or stand
    /// whole on one line in a call's argument, however long.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("bool both = first && second;\n\nreturn both;")]
    [InlineData("return first\n    && name.StartsWith(value: \"a rather long prefix that has to be compared\", StringComparison.Ordinal)\n    && fallback.EndsWith(value: \"and a suffix\", StringComparison.Ordinal);")]
    [InlineData("if (first && name.StartsWith(value: \"a rather long prefix that has to be compared\", StringComparison.Ordinal) && fallback.EndsWith(value: \"and a suffix\", StringComparison.Ordinal))\n    return true;\n\nreturn false;")]
    [InlineData("bool both = first // The first flag gates everything.\n    && second;\n\nreturn both;")]
    [InlineData("return fallback.Contains(first ? name.Replace(oldValue: \"a placeholder in the name\", newValue: \"its value\", StringComparison.Ordinal) : fallback.Trim(), StringComparison.Ordinal);")]
    public async Task KeepsOtherExpressions(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new OperatorLayoutAnalyzer(), Wrap(statements)));
    }

    /// <summary>
    /// Verifies the fix joins an expression that fits, wherever its operators were written, and splits one that does
    /// not before every operator: a logical sequence, a conditional expression, and a coalescing sequence. One in a
    /// call's argument comes back onto one line whole.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("bool both = first\n    && second;\n\nreturn both;", "bool both = first && second;\n\nreturn both;")]
    [InlineData("bool both = first &&\n    second;\n\nreturn both;", "bool both = first && second;\n\nreturn both;")]
    [InlineData("return first && name.StartsWith(value: \"a rather long prefix that has to be compared\", StringComparison.Ordinal) && fallback.EndsWith(value: \"and a suffix\", StringComparison.Ordinal);",
        "return first\n    && name.StartsWith(value: \"a rather long prefix that has to be compared\", StringComparison.Ordinal)\n    && fallback.EndsWith(value: \"and a suffix\", StringComparison.Ordinal);")]
    [InlineData("string chosen = first ? name.Replace(oldValue: \"a placeholder in the name\", newValue: \"its value\", StringComparison.Ordinal) : fallback.Trim();\n\nreturn chosen.Length > 0;",
        "string chosen = first\n    ? name.Replace(oldValue: \"a placeholder in the name\", newValue: \"its value\", StringComparison.Ordinal)\n    : fallback.Trim();\n\nreturn chosen.Length > 0;")]
    [InlineData("string chosen = Environment.GetEnvironmentVariable(variable: \"FIRST_CHOICE\") ?? Environment.GetEnvironmentVariable(variable: \"SECOND_CHOICE\") ?? fallback;\n\nreturn chosen.Length > 0;",
        "string chosen = Environment.GetEnvironmentVariable(variable: \"FIRST_CHOICE\")\n    ?? Environment.GetEnvironmentVariable(variable: \"SECOND_CHOICE\")\n    ?? fallback;\n\nreturn chosen.Length > 0;")]
    [InlineData("return fallback.Contains(first\n    ? name.Replace(oldValue: \"a placeholder in the name\", newValue: \"its value\", StringComparison.Ordinal)\n    : fallback.Trim(), StringComparison.Ordinal);",
        "return fallback.Contains(first ? name.Replace(oldValue: \"a placeholder in the name\", newValue: \"its value\", StringComparison.Ordinal) : fallback.Trim(), StringComparison.Ordinal);")]
    public async Task LaysOutOperators(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixOperators(Wrap(written), fixAll: true));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
