using System.Text.RegularExpressions;

using NetAgents.Analyzers.Readability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

/// <summary>
/// Covers string literals and interpolated strings joined with + on one line, and the fix that writes them as one.
/// </summary>
public sealed class LiteralConcatenationTests
{
    private const string Method = "public static class ExampleType\n{\n    public static string Run(string name, int count)\n    {\nBODY\n    }\n\n"
        + "    private static string Print(string value)\n    {\n        return value;\n    }\n}\n";

    /// <summary>
    /// Verifies pieces stay as written when one is not a plain string, they are split over lines or separated by a
    /// comment, or merging would let an escape run into the next piece.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("return \"first \"\n    + \"second\";")]
    [InlineData("return @\"first \" + \"second\";")]
    [InlineData("return \"\"\"first \"\"\" + \"second\";")]
    [InlineData("return name + \"second\";")]
    [InlineData("return \"\\x1\" + \"2\";")]
    [InlineData("return \"first \" /* joined */ + \"second\";")]
    public async Task KeepsOtherConcatenations(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new LiteralConcatenationAnalyzer(), Wrap(statements)));
    }

    /// <summary>
    /// Verifies literals and interpolated strings merge with their escapes and holes as written, braces from a literal
    /// doubled inside an interpolated string, every piece of a longer run, an argument, and the leading pieces of an
    /// addition that goes on with another operand.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("return \"first \" + \"second\\n\";", "return \"first second\\n\";")]
    [InlineData("return $\"{name} has \" + $\"{count} items\";", "return $\"{name} has {count} items\";")]
    [InlineData("return \"{\" + $\"{name}\" + \"}\";", "return $\"{{{name}}}\";")]
    [InlineData("return \"a\" + \"b\" + \"c\";", "return \"abc\";")]
    [InlineData("return Print($\"{name}\" + $\" and {count}\");", "return Print($\"{name} and {count}\");")]
    [InlineData("return \"count: \" + \"total \" + count;", "return \"count: total \" + count;")]
    public async Task WritesPiecesAsOneString(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixLiteralConcatenations(Wrap(written), fixAll: true));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
