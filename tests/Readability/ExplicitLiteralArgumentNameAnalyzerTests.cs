using NetAgents.Analyzers.Readability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

/// <summary>
/// Covers named arguments for inline literals and positional arguments everywhere else.
/// </summary>
public sealed class ExplicitLiteralArgumentNameAnalyzerTests
{
    /// <summary>
    /// Verifies member-access arguments are accepted when they are passed positionally.
    /// </summary>
    [Fact]
    public async Task AllowsMemberAccessArguments()
    {
        string source = CreateCoordinateSource(arguments: "boy.StateTargetCoordinate.First, boy.StateTargetCoordinate.Second");

        Assert.Empty(await AnalyzerTestHarness.Analyze(new ExplicitLiteralArgumentNameAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that explicitly named literal arguments are accepted.
    /// </summary>
    [Fact]
    public async Task AllowsNamedArguments()
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new ExplicitLiteralArgumentNameAnalyzer(),
            source: "public class ExampleType { public void Run() => Execute(count: 42); private void Execute(int count) { } }"
        );

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies that a name whose removal would change binding or evaluation order is accepted.
    /// </summary>
    /// <param name="invocation">The invocation carrying an unremovable name.</param>
    [Theory]
    [InlineData("Execute(first, third: third)")]
    [InlineData("Execute(second: second, first: first)")]
    [InlineData("Log(values: numbers)")]
    public async Task AllowsNamesThatCannotBeRemoved(string invocation)
    {
        string source = $$"""
            class ExampleType
            {
                void Run(int first, int second, int third, int[] numbers) => {{invocation}};
                void Execute(int first, int second = 0, int third = 0) { }
                void Log(params int[] values) { }
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new ExplicitLiteralArgumentNameAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that a name the rule does not require is reported.
    /// </summary>
    [Fact]
    public async Task ReportsRemovableMemberAccessNames()
    {
        string source = CreateCoordinateSource(arguments: "horizontalCoordinate: boy.StateTargetCoordinate.First, verticalCoordinate: boy.StateTargetCoordinate.Second");

        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ExplicitLiteralArgumentNameAnalyzer(), source);

        Assert.Equal(expected: 2, diagnostics.Length);
        Assert.All(diagnostics, static diagnostic => Assert.Equal(ExplicitLiteralArgumentNameAnalyzer.RuleIdentifier, diagnostic.Id));
    }

    /// <summary>
    /// Verifies that literal arguments without parameter names are reported.
    /// </summary>
    /// <param name="invocation">The invocation containing an unnamed literal.</param>
    [Theory]
    [InlineData("Execute(42)")]
    [InlineData("Execute(-42)")]
    [InlineData("Execute((42))")]
    [InlineData("Execute(-(42))")]
    [InlineData("Execute((-42))")]
    public async Task ReportsUnnamedLiteralArguments(string invocation)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new ExplicitLiteralArgumentNameAnalyzer(),
            $"public class ExampleType {{ public void Run() => {invocation}; private void Execute(int count) {{ }} }}"
        );

        Assert.Equal(ExplicitLiteralArgumentNameAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    private static string CreateCoordinateSource(string arguments)
    {
        return $$"""
            class ExampleType
            {
                ExampleType GameObject => this;
                (int First, int Second) StateTargetCoordinate => (1, 2);
                void Execute(ExampleType boy) => boy.GameObject.MoveTo({{arguments}});
                void MoveTo(int horizontalCoordinate, int verticalCoordinate) { }
            }
            """;
    }
}
