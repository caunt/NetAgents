using NetAgents.Analyzers.Readability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

/// <summary>
/// Covers reading members of a parenthesized <c>await</c>, which must go through a named local instead.
/// </summary>
public sealed class AwaitedValueAccessTests
{
    private const string Type = "using System.Threading.Tasks;\n\npublic static class ExampleType\n{\n    public static async Task<int> Run(Task<string> text, Task<int[]> numbers)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies an awaited value named first is accepted, as are parentheses around an await that nothing reads a
    /// member of.
    /// </summary>
    /// <param name="body">A method body reading an awaited value.</param>
    [Theory]
    [InlineData("        string value = await text;\n\n        return value.Length;")]
    [InlineData("        int[] values = await numbers;\n\n        return values.Length;")]
    [InlineData("        return (await text) is null ? 0 : 1;")]
    [InlineData("        return 1 + (await numbers is { Length: > 0 } ? 1 : 0);")]
    public async Task AcceptsNamedAwaitedValues(string body)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new AwaitedValueAccessAnalyzer(), Type.Replace(oldValue: "BODY", body, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Verifies member access, element access, and conditional access on a parenthesized await are each reported.
    /// </summary>
    /// <param name="body">A method body reading an awaited value.</param>
    [Theory]
    [InlineData("        return (await text).Length;")]
    [InlineData("        return (await numbers)[0];")]
    [InlineData("        return (await text)?.Length ?? 0;")]
    public async Task ReportsMembersOfParenthesizedAwaits(string body)
    {
        string source = Type.Replace(oldValue: "BODY", body, StringComparison.Ordinal);

        Assert.Equal(AwaitedValueAccessAnalyzer.RuleIdentifier,
            Assert.Single(await AnalyzerTestHarness.Analyze(new AwaitedValueAccessAnalyzer(), source)).Id);
    }
}
