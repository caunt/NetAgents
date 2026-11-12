using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.Analyzers.TypeSafety;

namespace NetAgents.Analyzers.Tests.TypeSafety;

/// <summary>
/// Covers numeric casts and implicit numeric conversions, and the conversions the rule leaves alone.
/// </summary>
public sealed class NumericConversionAnalyzerTests
{
    /// <summary>
    /// Verifies constants, char code points, nullable, enum and identity conversions, and expression trees
    /// are accepted.
    /// </summary>
    /// <param name="member">A member whose conversions the rule leaves alone.</param>
    [Theory]
    [InlineData("long Constant() => 5;")]
    [InlineData("const long Limit = int.MaxValue;")]
    [InlineData("byte Folded() => (byte)(200 + 55);")]
    [InlineData("long Shifted(int value) => (long)1 << value;")]
    [InlineData("int Code(char character) => character;")]
    [InlineData("char Letter(int value) => (char)value;")]
    [InlineData("int Plain(int value) => value;")]
    [InlineData("long? Lifted(int? value) => value;")]
    [InlineData("int Order(System.DayOfWeek day) => (int)day;")]
    [InlineData("System.Linq.Expressions.Expression<System.Func<int, long>> Tree() => value => value;")]
    public async Task AllowsConversionsWithoutAFactory(string member)
    {
        Assert.Empty(await Analyze(member));
    }

    /// <summary>
    /// Verifies every numeric cast and implicit numeric conversion of a value computed at run time is
    /// reported, including promotions in arithmetic.
    /// </summary>
    /// <param name="member">A member converting numbers.</param>
    /// <param name="expectedCount">The number of conversions reported.</param>
    [Theory]
    [InlineData("long Widen(int value) => value;", "1")]
    [InlineData("int Narrow(long value) => (int)value;", "1")]
    [InlineData("int Truncate(double value) => (int)value;", "1")]
    [InlineData("float Single(double value) => (float)value;", "1")]
    [InlineData("decimal Money(int value) => value;", "1")]
    [InlineData("nint Native(int value) => value;", "1")]
    [InlineData("void Run(int value) { long total = value; }", "1")]
    [InlineData("long Pick(bool flag, int value, long other) => flag ? value : other;", "1")]
    [InlineData("double Ratio(int count, int total) => (double)count / total;", "2")]
    [InlineData("int Sum(byte first, byte second) => first + second;", "2")]
    [InlineData("long Twice(long value) => (int)value;", "2")]
    public async Task ReportsNumericConversions(string member, string expectedCount)
    {
        Diagnostic[] diagnostics = await Analyze(member);

        Assert.Equal(int.Parse(expectedCount, System.Globalization.CultureInfo.InvariantCulture), diagnostics.Length);
        Assert.All(diagnostics, static diagnostic => Assert.Equal(NumericConversionAnalyzer.RuleIdentifier, diagnostic.Id));
    }

    private static async Task<Diagnostic[]> Analyze(string member)
    {
        return await AnalyzerTestHarness.Analyze(new NumericConversionAnalyzer(), $"public sealed class ExampleType {{ {member} }}")
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
