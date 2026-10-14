using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>
/// Covers the parentheses a lambda with one inferred parameter must omit, and the fix that drops them.
/// </summary>
public sealed class LambdaParameterLayoutTests
{
    /// <summary>
    /// Verifies that parentheses the language requires are accepted.
    /// </summary>
    /// <param name="target">The delegate type the lambda is converted to.</param>
    /// <param name="lambda">The lambda whose parentheses are required.</param>
    [Theory]
    [InlineData("Func<int>", "() => 0")]
    [InlineData("Func<int, int>", "(int value) => value")]
    [InlineData("Func<int, int, int>", "(value, other) => value + other")]
    [InlineData("Func<int, int>", "int (value) => value")]
    [InlineData("RefHandler", "static (ref int value) => value")]
    public async Task AllowsRequiredParentheses(string target, string lambda)
    {
        string source = $$"""
            using System;

            delegate int RefHandler(ref int value);

            class ExampleType
            {
                {{target}} Run() => {{lambda}};
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new LambdaParameterLayoutAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that a lambda already written without parentheses is accepted.
    /// </summary>
    [Fact]
    public async Task AllowsSimpleLambda()
    {
        const string source = """
            using System;

            class ExampleType
            {
                Func<int, int> Run() => static value => value;
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new LambdaParameterLayoutAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that every optional pair in a document is dropped in one pass.
    /// </summary>
    [Fact]
    public async Task DropsEveryOptionalPair()
    {
        const string source = """
            using System;

            class ExampleType
            {
                Func<int, int> First() => (value) => value;
                Func<int, int> Second() => static (other) => other;
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixLambdaParameters(source, fixAll: true);

        Assert.Contains(expectedSubstring: "First() => value => value;", fixedSource, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "Second() => static other => other;", fixedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the surrounding layout and the lambda body are preserved.
    /// </summary>
    /// <param name="lambda">The lambda to fix.</param>
    /// <param name="expected">The expected lambda after the fix.</param>
    [Theory]
    [InlineData("(value) => value", "value => value")]
    [InlineData("static (value) => value", "static value => value")]
    [InlineData("(_) => 0", "_ => 0")]
    [InlineData("( value ) => value", "value => value")]
    [InlineData("(value) => { return value; }", "value => { return value; }")]
    [InlineData("(/* keep */ value) => value", "/* keep */ value => value")]
    public async Task DropsOptionalParentheses(string lambda, string expected)
    {
        string source = $$"""
            using System;

            class ExampleType
            {
                Func<int, int> Run() => {{lambda}};
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixLambdaParameters(source, fixAll: true);

        Assert.Equal(source.Replace(lambda, expected, StringComparison.Ordinal), fixedSource);
    }

    /// <summary>
    /// Verifies that optional parentheses around one inferred parameter are reported.
    /// </summary>
    /// <param name="lambda">The lambda carrying optional parentheses.</param>
    [Theory]
    [InlineData("(value) => value")]
    [InlineData("static (value) => value")]
    [InlineData("(_) => 0")]
    public async Task ReportsOptionalParentheses(string lambda)
    {
        string source = $$"""
            using System;

            class ExampleType
            {
                Func<int, int> Run() => {{lambda}};
            }
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new LambdaParameterLayoutAnalyzer(), source);

        Assert.Equal(LambdaParameterLayoutAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }
}
