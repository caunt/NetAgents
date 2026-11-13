using System.Text.RegularExpressions;

using NetAgents.Analyzers.CodeFixes.Formatting;
using NetAgents.Analyzers.Formatting.Clauses;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting.Clauses;

/// <summary>
/// Covers the <c>when</c> keyword of catch filters, case guards, and switch expression arm guards, and the fix that
/// moves it up behind its clause.
/// </summary>
public sealed class WhenClausePlacementTests
{
    private const string Method = "public sealed class ExampleType\n{\n    public int Execute(object value, bool flag)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies <c>when</c> on the line of its clause is accepted in all three places, including a catch clause
    /// without a declaration and a guard whose condition goes on over further lines.
    /// </summary>
    /// <param name="statements">A method body whose guard sits on the line of its clause.</param>
    [Theory]
    [InlineData("try\n{\n    return 1;\n}\ncatch (System.InvalidOperationException exception) when (exception.Data.Count > 0)\n{\n    return 0;\n}")]
    [InlineData("try\n{\n    return 1;\n}\ncatch when (flag)\n{\n    return 0;\n}")]
    [InlineData("switch (value)\n{\n    case int number when number > 0:\n        return number;\n    default:\n        return 0;\n}")]
    [InlineData("switch (value)\n{\n    case int number when number > 0\n        && flag:\n        return number;\n    default:\n        return 0;\n}")]
    [InlineData("return value switch\n{\n    int number when number > 0 => number,\n    _ => 0,\n};")]
    public async Task AcceptsWhenOnTheGuardedLine(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new WhenClausePlacementAnalyzer(), Wrap(statements)));
    }

    /// <summary>
    /// Verifies a comment between the clause and <c>when</c> is reported but not moved, since only its author knows
    /// where it belongs.
    /// </summary>
    [Fact]
    public async Task LeavesCommentsToTheAuthor()
    {
        string source = Wrap(statements: "try\n{\n    return 1;\n}\ncatch (System.InvalidOperationException exception) // Only the retried kind.\n    when (exception.Data.Count > 0)\n{\n    return 0;\n}");

        Assert.Equal(WhenClausePlacementAnalyzer.RuleIdentifier,
            Assert.Single(await AnalyzerTestHarness.Analyze(new WhenClausePlacementAnalyzer(), source)).Id);

        Assert.Equal(expected: 0,
            await CodeFixTestHarness.CountFixes(source, new WhenClausePlacementAnalyzer(), new WhenClausePlacementCodeFixProvider()));
    }

    /// <summary>
    /// Verifies a <c>when</c> that starts a line is moved up behind its clause with one space: after the parentheses
    /// of a catch clause, after a bare <c>catch</c>, after a case pattern, and after a switch arm's pattern.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("try\n{\n    return 1;\n}\ncatch (System.InvalidOperationException exception)\n    when (exception.Data.Count > 0)\n{\n    return 0;\n}",
        "try\n{\n    return 1;\n}\ncatch (System.InvalidOperationException exception) when (exception.Data.Count > 0)\n{\n    return 0;\n}")]
    [InlineData("try\n{\n    return 1;\n}\ncatch\n    when (flag)\n{\n    return 0;\n}",
        "try\n{\n    return 1;\n}\ncatch when (flag)\n{\n    return 0;\n}")]
    [InlineData("switch (value)\n{\n    case int number\n        when number > 0:\n        return number;\n    default:\n        return 0;\n}",
        "switch (value)\n{\n    case int number when number > 0:\n        return number;\n    default:\n        return 0;\n}")]
    [InlineData("return value switch\n{\n    int number\n        when number > 0 => number,\n    string text\n\n        when text.Length > 0 => text.Length,\n    _ => 0,\n};",
        "return value switch\n{\n    int number when number > 0 => number,\n    string text when text.Length > 0 => text.Length,\n    _ => 0,\n};")]
    public async Task MovesWhenUp(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixWhenClauses(Wrap(written), fixAll: true));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
