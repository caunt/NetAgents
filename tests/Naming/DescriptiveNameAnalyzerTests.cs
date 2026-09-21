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
    /// Verifies protocol and format names stay usable as the domain spells them.
    /// </summary>
    /// <param name="parameterName">The protocol or format parameter name to accept.</param>
    [Theory]
    [InlineData("httpClient")]
    [InlineData("apiKey")]
    [InlineData("requestUri")]
    [InlineData("downloadUrl")]
    [InlineData("tcpListener")]
    [InlineData("udpEndpoint")]
    [InlineData("xmlDocument")]
    [InlineData("utfEncoding")]
    [InlineData("httpsRedirect")]
    public async Task AllowsProtocolAndFormatNames(string parameterName)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new DescriptiveNameAnalyzer(), $"public class ExampleType {{ public void Execute(string {parameterName}) {{ }} }}");

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies standard .NET async names are accepted without requiring expanded words.
    /// </summary>
    /// <param name="source">A declaration using the standard Async word.</param>
    [Theory]
    [InlineData("class ExampleType { public async System.Threading.Tasks.Task ReadAsync() { await System.Threading.Tasks.Task.Yield(); } }")]
    [InlineData("class ExampleType { public System.Threading.Tasks.Task ReadAsync() => System.Threading.Tasks.Task.CompletedTask; }")]
    [InlineData("interface IExampleService { System.Threading.Tasks.ValueTask ReadAsync(); }")]
    [InlineData("class ExampleType { void Execute() { System.Threading.Tasks.Task ReadAsync() => System.Threading.Tasks.Task.CompletedTask; } }")]
    [InlineData("interface IAsyncReader { System.Threading.Tasks.Task ReadAsync(); }")]
    public async Task AllowsStandardAsyncNames(string source)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new DescriptiveNameAnalyzer(), source));
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
    [InlineData("dbConnection")]
    public async Task RejectsAbbreviatedNames(string parameterName)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new DescriptiveNameAnalyzer(), $"public class ExampleType {{ public void Execute(string {parameterName}) {{ }} }}");

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == DescriptiveNameAnalyzer.RuleIdentifier);
    }

    /// <summary>
    /// Verifies the Async suffix does not exempt abbreviations elsewhere in a name.
    /// </summary>
    [Fact]
    public async Task RejectsAbbreviationsBeforeAsyncSuffix()
    {
        const string source = "class ExampleType { System.Threading.Tasks.Task ReadMsgAsync() => System.Threading.Tasks.Task.CompletedTask; }";

        Assert.Equal(
            DescriptiveNameAnalyzer.RuleIdentifier,
            Assert.Single(await AnalyzerTestHarness.Analyze(new DescriptiveNameAnalyzer(), source)).Id
        );
    }
}
