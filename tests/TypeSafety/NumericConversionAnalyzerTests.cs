using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.Analyzers.TypeSafety;

namespace NetAgents.Analyzers.Tests.TypeSafety;

/// <summary>
/// Covers numeric casts written in the source, and the conversions the rule leaves alone.
/// </summary>
public sealed class NumericConversionAnalyzerTests
{
    /// <summary>
    /// Verifies casts of constants, char code points, nullable values, enums, and casts inside expression trees
    /// are accepted, since no factory applies to them.
    /// </summary>
    /// <param name="member">A member casting where the rule has no factory to offer.</param>
    [Theory]
    [InlineData("byte Folded() => (byte)(200 + 55);")]
    [InlineData("long Shifted(int value) => (long)1 << value;")]
    [InlineData("char Letter(int value) => (char)value;")]
    [InlineData("int? Lifted(long? value) => (int?)value;")]
    [InlineData("int Order(System.DayOfWeek day) => (int)day;")]
    [InlineData("System.Linq.Expressions.Expression<System.Func<long, int>> Tree() => value => (int)value;")]
    public async Task AllowsCastsWithoutAFactory(string member)
    {
        Assert.Empty(await Analyze(member));
    }

    /// <summary>
    /// Verifies every implicit conversion is accepted, whether it widens a value into a variable, an argument,
    /// or a result, or widens the operands of a comparison, an equality, arithmetic, a shift, or a compound
    /// assignment, as in the frame comparison reported in issue 52.
    /// </summary>
    /// <param name="member">A member converting numbers without a cast.</param>
    [Theory]
    [InlineData("long Widen(int value) => value;")]
    [InlineData("decimal Money(int value) => value;")]
    [InlineData("void Run(int value) { long total = value; }")]
    [InlineData("void Call(int value) => Take(value); void Take(long total) { }")]
    [InlineData("long Pick(bool flag, int value, long other) => flag ? value : other;")]
    [InlineData("bool Same(byte first, byte second) => first == second;")]
    [InlineData("bool Before(ushort first, ushort second) => first < second;")]
    [InlineData("bool Empty(byte count) => count == 0;")]
    [InlineData("bool Within(int count, long limit) => count < limit;")]
    [InlineData("int Sum(byte first, byte second) => first + second;")]
    [InlineData("int Negate(byte value) => -value;")]
    [InlineData("int Shift(byte value) => value << 2;")]
    [InlineData("void Pack(byte flags, byte bit) { flags |= bit; }")]
    [InlineData("int Shared(byte[] expected, byte[] encoded) { int index = 0; while (index < expected.Length && expected[index] == encoded[index]) index++; return index; }")]
    public async Task AllowsImplicitConversions(string member)
    {
        Assert.Empty(await Analyze(member));
    }

    /// <summary>
    /// Verifies each numeric cast of a value computed at run time is reported once, narrowing or widening, and
    /// that nested casts are reported one by one.
    /// </summary>
    /// <param name="member">A member casting numbers.</param>
    /// <param name="expectedCount">How many casts the member writes.</param>
    [Theory]
    [InlineData("int Narrow(long value) => (int)value;", "1")]
    [InlineData("int Truncate(double value) => (int)value;", "1")]
    [InlineData("float Single(double value) => (float)value;", "1")]
    [InlineData("long Widen(int value) => (long)value;", "1")]
    [InlineData("double Ratio(int count, int total) => (double)count / total;", "1")]
    [InlineData("long Twice(long value) => (int)value;", "1")]
    [InlineData("int Through(double ratio) => (int)(long)ratio;", "2")]
    [InlineData("void Pack(byte flags, int offset) { flags |= (byte)(1 << offset); }", "1")]
    public async Task ReportsNumericCasts(string member, string expectedCount)
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
