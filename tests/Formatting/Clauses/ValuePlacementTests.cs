using System.Text.RegularExpressions;

using NetAgents.Analyzers.Formatting.Clauses;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting.Clauses;

/// <summary>
/// Covers an assigned value that starts below its =, and the fix that joins the two.
/// </summary>
public sealed class ValuePlacementTests
{
    private const string Method = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\npublic sealed class ExampleType\n{\n"
        + "    public int Total { get; set; }\n\n    public int Run(int[] values)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies the fix joins the value of a declaration, an assignment, and a compound assignment to its =.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("int total =\n    values.Length + 1;\n\nreturn total;", "int total = values.Length + 1;\n\nreturn total;")]
    [InlineData("Total =\n    values.Length;\n\nreturn Total;", "Total = values.Length;\n\nreturn Total;")]
    [InlineData("Total +=\n    values.Sum();\n\nreturn Total;", "Total += values.Sum();\n\nreturn Total;")]
    public async Task JoinsValueToEquals(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixValuePlacements(Wrap(written), fixAll: true));
    }

    /// <summary>
    /// Verifies values stay as written when they already start beside the =, follow a comment on its line, or are a
    /// collection expression or an initializer, whose opening NETAGENTS0020 places.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("int total = values.Length + 1;\n\nreturn total;")]
    [InlineData("int total = // The extra one counts the sentinel.\n    values.Length + 1;\n\nreturn total;")]
    [InlineData("int[] copies =\n[\n    values.Length,\n    values.Sum(),\n];\n\nreturn copies.Length;")]
    [InlineData("int[] copies =\n{\n    values.Length,\n    values.Sum()\n};\n\nreturn copies.Length;")]
    public async Task KeepsOtherValues(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new ValuePlacementAnalyzer(), Wrap(statements)));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
