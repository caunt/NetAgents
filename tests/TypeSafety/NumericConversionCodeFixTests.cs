using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.TypeSafety;

/// <summary>
/// Covers the fix that rewrites numeric casts into the factory keeping their behavior.
/// </summary>
public sealed class NumericConversionCodeFixTests
{
    /// <summary>
    /// Verifies each cast becomes the factory that behaves as it did: CreateChecked where nothing can overflow
    /// or an overflow already throws, CreateTruncating for any other cast.
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
    /// Verifies only the cast is rewritten and the implicit conversions around it stay as written: a cast whose
    /// result is widened again, a widening cast before division, a cast inside a compound assignment, and
    /// parentheses the call no longer needs.
    /// </summary>
    /// <param name="member">The member as written.</param>
    /// <param name="expected">The member after the fix.</param>
    [Theory]
    [InlineData("long Twice(long value) => (int)value;", "long Twice(long value) => int.CreateTruncating(value);")]
    [InlineData("double Ratio(int count, int total) => (double)count / total;",
        "double Ratio(int count, int total) => double.CreateChecked(count) / total;")]
    [InlineData("void Pack(byte flags, int offset) { flags |= (byte)(1 << offset); }",
        "void Pack(byte flags, int offset) { flags |= byte.CreateTruncating(1 << offset); }")]
    [InlineData("long Scale(long factor, int first, int second) => factor * ((long)(first + second));",
        "long Scale(long factor, int first, int second) => factor * long.CreateChecked(first + second);")]
    public async Task RewritesOnlyTheCast(string member, string expected)
    {
        string source = $"public sealed class ExampleType {{ {member} }}";
        string target = $"public sealed class ExampleType {{ {expected} }}";

        Assert.Equal(target, await CodeFixTestHarness.FixNumericConversions(source, fixAll: true));
    }
}
