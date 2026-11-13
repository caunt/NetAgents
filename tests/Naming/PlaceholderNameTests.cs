using NetAgents.Analyzers.Naming;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Naming;

/// <summary>
/// Covers locals named only with placeholder words, such as <c>result</c> or <c>isConditionMet</c>.
/// </summary>
public sealed class PlaceholderNameTests
{
    private const string Type = "using System;\n\npublic sealed class ExampleType\n{\n    private int _result;\n\n    public int Result => _result;\n\n"
        + "    public int Run(int result, int[] values, object boxed)\n    {\n        BODY\n        return result;\n    }\n}\n";

    /// <summary>
    /// Verifies names that say what a value is are accepted even when they contain a placeholder word, and that
    /// parameters, fields, and properties are left to other rules.
    /// </summary>
    /// <param name="body">Statements declaring locals.</param>
    [Theory]
    [InlineData("int parseResult = values.Length;")]
    [InlineData("bool hasNoVersions = values.Length == 0;")]
    [InlineData("int results = values.Length;")]
    [InlineData("_result = result;")]
    public async Task AcceptsDescriptiveNames(string body)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new PlaceholderNameAnalyzer(), Type.Replace(oldValue: "BODY", body, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Verifies a local built only from placeholder words is reported wherever it is declared: a declaration, a
    /// <c>foreach</c> or <c>catch</c> variable, an <c>out</c> variable, a pattern, and a deconstruction, including
    /// combinations of placeholder words and a number.
    /// </summary>
    /// <param name="body">Statements declaring one local with a placeholder name.</param>
    [Theory]
    [InlineData("bool isConditionMet = values.Length == 0;")]
    [InlineData("int temp = values.Length;")]
    [InlineData("bool flag = values.Length > 0;")]
    [InlineData("int result2 = values.Length;")]
    [InlineData("int tempResult = values.Length;")]
    [InlineData("foreach (int condition in values) { }")]
    [InlineData("try { } catch (InvalidOperationException temp) { Console.WriteLine(temp.Message); }")]
    [InlineData("bool parsed = int.TryParse(s: \"1\", out int flag);")]
    [InlineData("if (boxed is int temp) { }")]
    [InlineData("(int result1, int count) = (1, 2);")]
    public async Task ReportsPlaceholderNames(string body)
    {
        string source = Type.Replace(oldValue: "BODY", body, StringComparison.Ordinal);

        Assert.Equal(PlaceholderNameAnalyzer.RuleIdentifier,
            Assert.Single(await AnalyzerTestHarness.Analyze(new PlaceholderNameAnalyzer(), source)).Id);
    }
}
