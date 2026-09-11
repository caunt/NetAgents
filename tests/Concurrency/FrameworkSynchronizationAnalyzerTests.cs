using NetAgents.Analyzers.Concurrency;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Concurrency;

public sealed class FrameworkSynchronizationAnalyzerTests
{
    [Theory]
    [InlineData("using System.Threading;", "public SemaphoreSlim Gate { get; } = new(1);")]
    [InlineData("using Gate = System.Threading.SemaphoreSlim;", "public Gate CreateGate() => new(1);")]
    [InlineData("using static System.Threading.Thread;", "public void Execute() => Sleep(1);")]
    [InlineData("using System.Threading.Tasks;", "public int Execute(Task<int> operation) => operation.Result;")]
    [InlineData("using System.Threading.Tasks;", "public void Execute(Task operation) => operation.GetAwaiter().GetResult();")]
    public async Task ReportsDirectAliasedAndStaticSynchronization(string imports, string memberSource)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new FrameworkSynchronizationAnalyzer(), $"{imports} public class ExampleType {{ {memberSource} }}");

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == FrameworkSynchronizationAnalyzer.RuleIdentifier);
    }

    [Fact]
    public async Task AllowsAtomicsAndAsynchronousComposition()
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new FrameworkSynchronizationAnalyzer(), source: "public class ExampleType { private int count; public async System.Threading.Tasks.Task Execute() { System.Threading.Interlocked.Increment(ref count); await System.Threading.Tasks.Task.Delay(1); } }");

        Assert.Empty(diagnostics);
    }
}
