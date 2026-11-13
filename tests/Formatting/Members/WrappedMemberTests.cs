using NetAgents.Analyzers.Formatting.Members;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting.Members;

/// <summary>
/// Covers members wrapped at the <c>=</c> of an initializer or the <c>=&gt;</c> of an expression body, and the fix
/// that joins them.
/// </summary>
public sealed class WrappedMemberTests
{
    /// <summary>
    /// Verifies members are accepted when they are already on one line, when joining would pass 128 characters, when
    /// they span lines somewhere besides the operator, and when a comment sits beside the operator.
    /// </summary>
    /// <param name="member">A member left as written.</param>
    [Theory]
    [InlineData("    private int _count = 1;")]
    [InlineData("    private readonly string _text =\n        \"Long enough that joining it with the declaration passes the one hundred and twenty-eight character limit.\";")]
    [InlineData("    private int Total(int first, int second) =>\n        System.Math.Max(first,\n            second);")]
    [InlineData("    private int _count = // Starts at one.\n        1;")]
    public async Task AcceptsMembersThatStayWrapped(string member)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new WrappedMemberAnalyzer(), Wrap(member)));
    }

    /// <summary>
    /// Verifies a member that fits on one line is joined with one space on each side of its operator: a field and a
    /// property initializer, a property and a method body wrapped after the arrow, and one wrapped before it.
    /// </summary>
    /// <param name="written">The member as written.</param>
    /// <param name="expected">The member after the fix.</param>
    [Theory]
    [InlineData("    private readonly int _count =\n        1;", "    private readonly int _count = 1;")]
    [InlineData("    public byte[] Key { get; init; } =\n        System.Array.Empty<byte>();",
        "    public byte[] Key { get; init; } = System.Array.Empty<byte>();")]
    [InlineData("    public int Twice =>\n        2;", "    public int Twice => 2;")]
    [InlineData("    public int Sum(int first, int second) =>\n        first + second;",
        "    public int Sum(int first, int second) => first + second;")]
    [InlineData("    public int Sum(int first, int second)\n        => first + second;",
        "    public int Sum(int first, int second) => first + second;")]
    public async Task JoinsMembersThatFit(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixWrappedMembers(Wrap(written), fixAll: true));
    }

    private static string Wrap(string member)
    {
        return $"public sealed class ExampleType\n{{\n{member}\n}}\n";
    }
}
