using NetAgents.Analyzers.Readability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

/// <summary>
/// Covers named arguments for inline literals and default values.
/// </summary>
public sealed class ExplicitLiteralArgumentNameAnalyzerTests
{

    /// <summary>
    /// Verifies member-access arguments do not require parameter names.
    /// </summary>
    /// <param name="arguments">The named or positional coordinate arguments.</param>
    [Theory]
    [InlineData("boy.StateTargetCoordinate.First, boy.StateTargetCoordinate.Second")]
    [InlineData("horizontalCoordinate: boy.StateTargetCoordinate.First, verticalCoordinate: boy.StateTargetCoordinate.Second")]
    public async Task AllowsMemberAccessArguments(string arguments)
    {
        string source = $$"""
            class ExampleType
            {
                ExampleType GameObject => this;
                (int First, int Second) StateTargetCoordinate => (1, 2);
                void Execute(ExampleType boy) => boy.GameObject.MoveTo({{arguments}});
                void MoveTo(int horizontalCoordinate, int verticalCoordinate) { }
            }
            """;

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
    /// Verifies that literal arguments without parameter names are reported.
    /// </summary>
    /// <param name="invocation">The invocation containing an unnamed literal.</param>
    [Theory]
    [InlineData("Execute(42)")]
    [InlineData("Execute(-42)")]
    [InlineData("Execute((42))")]
    public async Task ReportsUnnamedLiteralArguments(string invocation)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new ExplicitLiteralArgumentNameAnalyzer(),
            $"public class ExampleType {{ public void Run() => {invocation}; private void Execute(int count) {{ }} }}"
        );

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == ExplicitLiteralArgumentNameAnalyzer.RuleIdentifier);
    }
}
