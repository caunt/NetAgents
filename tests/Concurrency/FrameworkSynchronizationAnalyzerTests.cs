using NetAgents.Analyzers.Concurrency;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Concurrency;

/// <summary>
/// Covers prohibited synchronization APIs and permitted atomic operations.
/// </summary>
public sealed class FrameworkSynchronizationAnalyzerTests
{
    /// <summary>
    /// Verifies that aliases and static imports cannot bypass synchronization checks.
    /// </summary>
    /// <param name="imports">Using directives that expose the synchronization API.</param>
    /// <param name="memberSource">The member containing the prohibited operation.</param>
    [Theory]
    [InlineData("using System.Threading;", "public SemaphoreSlim Gate { get; } = new(1);")]
    [InlineData("using Gate = System.Threading.SemaphoreSlim;", "public Gate CreateGate() => new(1);")]
    [InlineData("using static System.Threading.Thread;", "public void Execute() => Sleep(1);")]
    [InlineData("using System.Threading.Tasks;", "public int Execute(Task<int> operation) => operation.Result;")]
    [InlineData("using System.Threading.Tasks;", "public void Execute(Task operation) => operation.GetAwaiter().GetResult();")]
    public async Task ReportsDirectAliasedAndStaticSynchronization(string imports, string memberSource)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new FrameworkSynchronizationAnalyzer(), $"{imports} public class ExampleType {{ {memberSource} }}");

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == FrameworkSynchronizationAnalyzer.RuleIdentifier);
    }

    /// <summary>
    /// Verifies that atomic operations and asynchronous task composition remain available.
    /// </summary>
    [Fact]
    public async Task AllowsAtomicsAndAsynchronousComposition()
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new FrameworkSynchronizationAnalyzer(),
            source: "public class ExampleType { private int count; public async System.Threading.Tasks.Task Execute() { System.Threading.Interlocked.Increment(ref count); await System.Threading.Tasks.Task.Delay(1); } }"
        );

        Assert.Empty(diagnostics);
    }
}
