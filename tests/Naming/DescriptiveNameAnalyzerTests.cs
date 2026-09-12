using NetAgents.Analyzers.Naming;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Naming;

/// <summary>
/// Covers descriptive identifiers and supported language naming conventions.
/// </summary>
public sealed class DescriptiveNameAnalyzerTests
{

    /// <summary>
    /// Verifies conventional interface and generic parameter prefixes are accepted.
    /// </summary>
    [Fact]
    public async Task AllowsDescriptiveGenericAndInterfacePrefixes()
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new DescriptiveNameAnalyzer(), source: "public interface IMessageHandler<TMessage> { void Handle(TMessage message); }");

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies that shortened parameter names produce a diagnostic.
    /// </summary>
    /// <param name="parameterName">The abbreviated parameter name to reject.</param>
    [Theory]
    [InlineData("x")]
    [InlineData("ctx")]
    [InlineData("configurationDto")]
    [InlineData("requestURL")]
    [InlineData("httpClient")]
    public async Task RejectsAbbreviatedNames(string parameterName)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new DescriptiveNameAnalyzer(), $"public class ExampleType {{ public void Execute(string {parameterName}) {{ }} }}");

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == DescriptiveNameAnalyzer.RuleIdentifier);
    }
}
