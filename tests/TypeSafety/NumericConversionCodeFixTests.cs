using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.TypeSafety;

/// <summary>
/// Covers the fix that rewrites numeric conversions into the factory keeping their behavior.
/// </summary>
public sealed class NumericConversionCodeFixTests
{
    /// <summary>
    /// Verifies each conversion becomes the factory that behaves as it did: CreateChecked where nothing can
    /// overflow or an overflow already throws, CreateTruncating for any other cast.
    /// </summary>
    /// <param name="expression">The expression as written, with long <c>value</c>, double <c>ratio</c>,
    /// decimal <c>amount</c>, and int <c>count</c> in scope.</param>
    /// <param name="expected">The expression after the fix.</param>
    [Theory]
    [InlineData("(int)value", "int.CreateTruncating(value)")]
    [InlineData("checked((int)value)", "checked(int.CreateChecked(value))")]
    [InlineData("(int)amount", "int.CreateChecked(amount)")]
    [InlineData("(int)ratio", "int.CreateTruncating(ratio)")]
    [InlineData("(int)(value + 1)", "int.CreateTruncating(value + 1)")]
    [InlineData("(int)(long)ratio", "int.CreateTruncating(long.CreateTruncating(ratio))")]
    [InlineData("(int)value + count", "int.CreateTruncating(value) + count")]
    public async Task ConvertsCasts(string expression, string expected)
    {
        const string member = "public sealed class ExampleType { public int Convert(long value, double ratio, decimal amount, int count) => EXPRESSION; }";

        string source = member.Replace(oldValue: "EXPRESSION", expression, StringComparison.Ordinal);
        string target = member.Replace(oldValue: "EXPRESSION", expected, StringComparison.Ordinal);

        Assert.Equal(target, await CodeFixTestHarness.FixNumericConversions(source, fixAll: true));
    }

    /// <summary>
    /// Verifies implicit conversions are wrapped in CreateChecked for the type they convert to, including
    /// both operands of a promotion and a cast whose result is converted again.
    /// </summary>
    /// <param name="member">The member as written.</param>
    /// <param name="expected">The member after the fix.</param>
    [Theory]
    [InlineData("long Widen(int value) => value;", "long Widen(int value) => long.CreateChecked(value);")]
    [InlineData("double Ratio(int count, int total) => (double)count / total;",
        "double Ratio(int count, int total) => double.CreateChecked(count) / double.CreateChecked(total);")]
    [InlineData("int Sum(byte first, byte second) => first + second;",
        "int Sum(byte first, byte second) => int.CreateChecked(first) + int.CreateChecked(second);")]
    [InlineData("long Twice(long value) => (int)value;", "long Twice(long value) => long.CreateChecked(int.CreateTruncating(value));")]
    [InlineData("void Run(int value) { long total = value; System.Console.WriteLine(total); }",
        "void Run(int value) { long total = long.CreateChecked(value); System.Console.WriteLine(total); }")]
    [InlineData("long Sum(int first, int second) => (first + second);", "long Sum(int first, int second) => long.CreateChecked(first + second);")]
    [InlineData("long Scale(long factor, int first, int second) => factor * ((first + second));",
        "long Scale(long factor, int first, int second) => factor * long.CreateChecked(first + second);")]
    [InlineData("long Total(byte first, byte second) => first + second;",
        "long Total(byte first, byte second) => long.CreateChecked(int.CreateChecked(first) + int.CreateChecked(second));")]
    [InlineData("long Order(System.DayOfWeek day) => (int)day;", "long Order(System.DayOfWeek day) => long.CreateChecked((int)day);")]
    [InlineData("void Call(int value) => Take(value); void Take(long total) { }",
        "void Call(int value) => Take(long.CreateChecked(value)); void Take(long total) { }")]
    [InlineData("long Add(long total, int value) { total += value; return total; }",
        "long Add(long total, int value) { total += long.CreateChecked(value); return total; }")]
    [InlineData("System.Func<int, long> Widen() => value => value;", "System.Func<int, long> Widen() => value => long.CreateChecked(value);")]
    [InlineData("long[] Values(int first) => [first];", "long[] Values(int first) => [long.CreateChecked(first)];")]
    public async Task WrapsImplicitConversions(string member, string expected)
    {
        string source = $"public sealed class ExampleType {{ {member} }}";
        string target = $"public sealed class ExampleType {{ {expected} }}";

        Assert.Equal(target, await CodeFixTestHarness.FixNumericConversions(source, fixAll: true));
    }
}
