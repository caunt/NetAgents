using NetAgents.Analyzers.Naming;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Naming;

public sealed class DescriptiveNameAnalyzerTests
{
    [Theory]
    [InlineData("x")]
    [InlineData("ctx")]
    [InlineData("configurationDto")]
    [InlineData("requestURL")]
    [InlineData("httpClient")]
    public async Task RejectsAbbreviatedNames(string parameterName)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new DescriptiveNameAnalyzer(), $"public class ExampleType {{ public void Execute(string {parameterName}) {{ }} }}");

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == DescriptiveNameAnalyzer.RuleIdentifier);
    }

    [Fact]
    public async Task AllowsDescriptiveGenericAndInterfacePrefixes()
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new DescriptiveNameAnalyzer(), source: "public interface IMessageHandler<TMessage> { void Handle(TMessage message); }");

        Assert.Empty(diagnostics);
    }
}