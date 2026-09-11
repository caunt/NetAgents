using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>
/// Verifies condition coverage, delimiter layout, and the exact length limit.
/// </summary>
public sealed class ConditionLayoutAnalyzerTests
{
    /// <summary>
    /// Rejects multiline conditions across supported control flow constructs.
    /// </summary>
    /// <param name="statement">The statement containing a condition violation.</param>
    [Theory]
    [InlineData("if (\nvalue > 0) return;")]
    [InlineData("if (value > 0\n) return;")]
    [InlineData("if (value > 0\r\n || value < -1) return;")]
    [InlineData("if (value > 0) return; else if (\nvalue < 0) return;")]
    [InlineData("while (value >\n0) value--;")]
    [InlineData("do value--; while (\nvalue > 0);")]
    [InlineData("for (; value >\n0; value--) { }")]
    [InlineData("value = value >\n0 ? 1 : 2;")]
    [InlineData("try { } catch (System.Exception) when (\nvalue > 0) { }")]
    [InlineData("switch (value) { case int number when\nnumber > 0: break; }")]
    [InlineData("value = value switch { int number when number >\n0 => 1, _ => 0 };")]
    [InlineData("if (value /* comment\ncomment */ > 0) return;")]
    public async Task RejectsMultilineConditions(string statement)
    {
        Diagnostic[] diagnostics = await Analyze(statement);

        Assert.Equal(ConditionLayoutAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
        Assert.Contains(
            expectedSubstring: "separate variable declaration",
            diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal
        );
    }

    /// <summary>
    /// Accepts short conditions and ignores unrelated multiline headers or expressions.
    /// </summary>
    /// <param name="statement">The statement to analyze.</param>
    [Theory]
    [InlineData("if (value > 0) return; else if (value < 0) return;")]
    [InlineData("while (value > 0) value--;")]
    [InlineData("do value--; while (value > 0);")]
    [InlineData("for (\nint index = 0; index < value;\nindex++) { }")]
    [InlineData("for (;;) break;")]
    [InlineData("value = value > 0\n? 1\n: 2;")]
    [InlineData("value = value > 0 ? System.Math.Abs(\nvalue) : System.Math.Abs(\n-value);")]
    [InlineData("try { } catch (System.Exception) when (value > 0) { }")]
    [InlineData("switch (value) { case int number when number > 0: break; }")]
    [InlineData("value = value switch { int number when number > 0 => 1, _ => 0 };")]
    [InlineData("bool result = value > 0\n|| value < -1; if (result) return;")]
    public async Task AcceptsValidConditions(string statement)
    {
        Assert.Empty(await Analyze(statement));
    }

    /// <summary>
    /// Counts exactly the characters inside parentheses, including whitespace.
    /// </summary>
    /// <param name="conditionLength">The length of the condition text.</param>
    [Theory]
    [InlineData("127")]
    [InlineData("128")]
    [InlineData("129")]
    public async Task EnforcesExactLengthBoundary(string conditionLength)
    {
        int length = int.Parse(conditionLength, System.Globalization.CultureInfo.InvariantCulture);
        string condition = "value > 0" + new string(c: ' ', length - "value > 0".Length);
        Diagnostic[] diagnostics = await Analyze($"if ({condition}) return;");

        Assert.Equal(length > 128 ? 1 : 0, diagnostics.Length);
    }

    /// <summary>
    /// Rejects long expressions in every supported condition position.
    /// </summary>
    /// <param name="template">A statement with a condition placeholder.</param>
    [Theory]
    [InlineData("if (CONDITION) return;")]
    [InlineData("while (CONDITION) value--;")]
    [InlineData("do value--; while (CONDITION);")]
    [InlineData("for (; CONDITION; value--) { }")]
    [InlineData("value = CONDITION ? 1 : 2;")]
    [InlineData("try { } catch (System.Exception) when (CONDITION) { }")]
    [InlineData("switch (value) { case int number when CONDITION: break; }")]
    [InlineData("value = value switch { int number when CONDITION => 1, _ => 0 };")]
    public async Task RejectsLongConditions(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        string condition = string.Join(separator: " || ", Enumerable.Repeat(element: "value > 0", count: 12));
        Diagnostic[] diagnostics = await Analyze(template.Replace(oldValue: "CONDITION", condition, StringComparison.Ordinal));

        Assert.Equal(ConditionLayoutAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    private static Task<Diagnostic[]> Analyze(string statement)
    {
        return AnalyzerTestHarness.Analyze(new ConditionLayoutAnalyzer(), $"public static class ExampleType {{ public static void Execute(int value) {{ {statement} }} }}");
    }
}
