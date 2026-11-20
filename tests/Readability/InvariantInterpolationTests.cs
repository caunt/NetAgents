using System.Text.RegularExpressions;

using NetAgents.Analyzers.Readability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

/// <summary>
/// Covers interpolated strings passed through string.Create with the invariant culture, and the fix that writes alone
/// the ones no culture can change.
/// </summary>
public sealed class InvariantInterpolationTests
{
    private const string Method = "using System;\nusing System.Diagnostics;\nusing System.Globalization;\n\npublic static class ExampleType\n{\n"
        + "    public static string Run(string name, int count, DayOfWeek day, DayOfWeek? rest, bool ready, char letter, Guid identifier)\n    {\nBODY\n    }\n\n"
        + "    private static string Print(string value)\n    {\n        return value;\n    }\n}\n";

    /// <summary>
    /// Verifies the call stays where a hole holds a number, a date, or a value of unknown type, where the provider is
    /// another culture, where a comment sits outside the string, and where the string alone would bind its call to an
    /// interpolated string handler.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("return string.Create(CultureInfo.InvariantCulture, $\"{name} has {count} items\");")]
    [InlineData("return string.Create(CultureInfo.InvariantCulture, $\"{name} at {DateTime.UtcNow}\");")]
    [InlineData("return string.Create(CultureInfo.InvariantCulture, $\"{name} costs {1.5}\");")]
    [InlineData("return string.Create(CultureInfo.InvariantCulture, $\"{(object)name} is ready\");")]
    [InlineData("return string.Create(CultureInfo.CurrentCulture, $\"{name} is ready\");")]
    [InlineData("return string.Create(CultureInfo.InvariantCulture, /* fixed */ $\"{name} is ready\");")]
    [InlineData("Debug.Assert(ready, string.Create(CultureInfo.InvariantCulture, $\"{name} is ready\"));\n\nreturn name;")]
    public async Task KeepsCallsThatCanChangeTheText(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new InvariantInterpolationAnalyzer(), Wrap(statements)));
    }

    /// <summary>
    /// Verifies the string is written alone when its holes hold strings, enumerations, nullable ones included, a bool,
    /// a char, or a Guid, with format clauses kept, whether the call is a value, an argument, split over lines, or
    /// names its arguments.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("return string.Create(CultureInfo.InvariantCulture, $\"{name} is ready\");", "return $\"{name} is ready\";")]
    [InlineData("return string.Create(CultureInfo.InvariantCulture, $\"ready\");", "return $\"ready\";")]
    [InlineData("return string.Create(CultureInfo.InvariantCulture, $\"{day} or {rest}\");", "return $\"{day} or {rest}\";")]
    [InlineData("return string.Create(CultureInfo.InvariantCulture, $\"{ready} {letter} {identifier:N}\");",
        "return $\"{ready} {letter} {identifier:N}\";")]
    [InlineData("return Print(string.Create(CultureInfo.InvariantCulture, $\"{name} is ready\"));", "return Print($\"{name} is ready\");")]
    [InlineData("throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $\"{name} failed\"));",
        "throw new InvalidOperationException($\"{name} failed\");")]
    [InlineData("return string.Create(CultureInfo.InvariantCulture,\n    $\"{name} is ready\");", "return $\"{name} is ready\";")]
    [InlineData("return string.Create(CultureInfo.InvariantCulture, handler: $\"{name} is ready\");", "return $\"{name} is ready\";")]
    [InlineData("return string.Create(handler: $\"{name} is ready\", provider: CultureInfo.InvariantCulture);", "return $\"{name} is ready\";")]
    public async Task WritesTheStringAlone(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixInvariantInterpolations(Wrap(written), fixAll: true));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
