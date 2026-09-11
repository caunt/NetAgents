using NetAgents.Analyzers.Readability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

public sealed class ExplicitLiteralArgumentNameAnalyzerTests
{
    [Theory]
    [InlineData("Execute(42)")]
    [InlineData("Execute(-42)")]
    [InlineData("Execute((42))")]
    public async Task ReportsUnnamedLiteralArguments(string invocation)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new ExplicitLiteralArgumentNameAnalyzer(), $"public class ExampleType {{ public void Run() => {invocation}; private void Execute(int count) {{ }} }}");

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == ExplicitLiteralArgumentNameAnalyzer.RuleIdentifier);
    }

    [Fact]
    public async Task AllowsNamedArguments()
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new ExplicitLiteralArgumentNameAnalyzer(), source: "public class ExampleType { public void Run() => Execute(count: 42); private void Execute(int count) { } }");

        Assert.Empty(diagnostics);
    }
}