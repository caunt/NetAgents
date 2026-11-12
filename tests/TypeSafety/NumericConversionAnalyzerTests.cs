using System.Globalization;

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
    /// Verifies a narrowing compound assignment whose target could change when read twice, such as an element
    /// reached through an incremented index or a property, is reported with no fix and told how to rewrite it.
    /// </summary>
    /// <param name="member">A member assigning through a target the fix cannot read a second time.</param>
    [Theory]
    [InlineData("void Mark(byte[] buffer, int index, byte bit) { buffer[index++] |= bit; }")]
    [InlineData("byte Level { get; set; } void Raise(byte step) { Level += step; }")]
    public async Task LeavesUnrepeatableTargetsToTheirAuthor(string member)
    {
        Diagnostic diagnostic = Assert.Single(await Analyze(member));

        Assert.True(diagnostic.Properties.ContainsKey(key: "Manual"));
        Assert.Contains(expectedSubstring: "plain assignment", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies every numeric cast and implicit numeric conversion of a value computed at run time is
    /// reported, including promotions in arithmetic, and that a compound assignment on a type narrower than
    /// int is reported once, for the narrowing of its result rather than the widening of its operand.
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
    [InlineData("void Pack(byte flags, byte bit) { flags |= bit; }", "1")]
    [InlineData("void Count(sbyte total) { total += 1; }", "1")]
    [InlineData("void Shift(ushort total, byte count) { total <<= count; }", "1")]
    [InlineData("void Scale(short total, short step) { checked { total *= step; } }", "1")]
    [InlineData("void Add(int total, byte step) { total += step; }", "1")]
    [InlineData("void Add(long total, int step) { total += step; }", "1")]
    public async Task ReportsNumericConversions(string member, string expectedCount)
    {
        Diagnostic[] diagnostics = await Analyze(member);

        Assert.Equal(int.Parse(expectedCount, CultureInfo.InvariantCulture), diagnostics.Length);
        Assert.All(diagnostics, static diagnostic => Assert.Equal(NumericConversionAnalyzer.RuleIdentifier, diagnostic.Id));
    }

    private static async Task<Diagnostic[]> Analyze(string member)
    {
        return await AnalyzerTestHarness.Analyze(new NumericConversionAnalyzer(), $"public sealed class ExampleType {{ {member} }}")
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
