using System.Text.RegularExpressions;

using NetAgents.Analyzers.Formatting.Clauses;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting.Clauses;

/// <summary>
/// Covers an expression-bodied lambda whose body starts below its arrow, and the fix that joins the two.
/// </summary>
public sealed class LambdaBodyPlacementTests
{
    private const string Method = "using System;\nusing System.Linq;\n\npublic static class ExampleType\n{\n"
        + "    public static int Run(int[] values)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies the fix joins the body of a simple and a parenthesized lambda to the arrow, in a declaration and in an
    /// argument, however long the joined line gets.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("Func<int, int> twice = value =>\n    value * 2;\n\nreturn twice(values.Length);",
        "Func<int, int> twice = value => value * 2;\n\nreturn twice(values.Length);")]
    [InlineData("Func<int, int, int> add = (left, right) =>\n    left + right;\n\nreturn add(values.Length, 1);",
        "Func<int, int, int> add = (left, right) => left + right;\n\nreturn add(values.Length, 1);")]
    [InlineData("return values.Count(value =>\nvalue > 0);", "return values.Count(value => value > 0);")]
    [InlineData("Func<int, int, int> pick = (firstCandidateFromTheLongerList, secondCandidateFromTheShorterList) =>\n    Math.Max(firstCandidateFromTheLongerList, secondCandidateFromTheShorterList) + values.Length;\n\nreturn pick(1, 2);",
        "Func<int, int, int> pick = (firstCandidateFromTheLongerList, secondCandidateFromTheShorterList) => Math.Max(firstCandidateFromTheLongerList, secondCandidateFromTheShorterList) + values.Length;\n\nreturn pick(1, 2);")]
    public async Task JoinsBodyToArrow(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixLambdaBodies(Wrap(written), fixAll: true));
    }

    /// <summary>
    /// Verifies lambdas stay as written when the body already starts beside the arrow, is a block, or follows a
    /// comment on the arrow's line.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("Func<int, int> twice = value => value * 2;\n\nreturn twice(values.Length);")]
    [InlineData("Func<int, int> twice = value =>\n{\n    return value * 2;\n};\n\nreturn twice(values.Length);")]
    [InlineData("Func<int, int> twice = value => // Doubling keeps the sign.\n    value * 2;\n\nreturn twice(values.Length);")]
    public async Task KeepsOtherLambdas(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new LambdaBodyPlacementAnalyzer(), Wrap(statements)));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
