using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Naming;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Naming;

/// <summary>
/// Covers method declarations whose name ends with a word their first parameter's name already says.
/// </summary>
public sealed class RedundantMethodSuffixAnalyzerTests
{
    /// <summary>
    /// Verifies a declaration is accepted when its name says something its first parameter's name does
    /// not, and that calls are never checked, whatever the variables they pass are called.
    /// </summary>
    /// <param name="members">The members declared in the type.</param>
    [Theory]
    [InlineData("public void Update(int value) { }")]
    [InlineData("public void UpdateValue(int count) { }")]
    [InlineData("public void UpdateValue() { }")]
    [InlineData("public void Log(string log) { }")]
    [InlineData("public void WithAnalyzer(int[] analyzers) { }")]
    [InlineData("public void Remove(Dick dick) { }")]
    [InlineData("public void SuckMyDick(Dick target) { }")]
    [InlineData("public void Run(int myValue) { Take(myValue); } public void Take(int value) { }")]
    [InlineData("public void Run(int value) { System.Console.WriteLine(value); }")]
    public async Task AllowsDistinctNames(string members)
    {
        Assert.Empty(await Analyze(members));
    }

    /// <summary>
    /// Verifies a method taking its name from elsewhere is accepted: an override, an implementation of an
    /// interface member, which is reported on the interface instead, and a framework interface's method.
    /// </summary>
    [Fact]
    public async Task ReportsOnlyWhereTheNameIsDeclared()
    {
        const string members = """
            public interface IUpdater
            {
                void UpdateValue(int value);
            }

            public class Updater : IUpdater, System.IFormattable
            {
                public void UpdateValue(int value) { }
                public string ToString(string? format, System.IFormatProvider? formatProvider) => string.Empty;
                public virtual void SetName(string name) { }
            }

            public sealed class Named : Updater
            {
                public override void SetName(string name) { }
            }
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new RedundantMethodSuffixAnalyzer(), members);

        Assert.Equal(expected: 2, diagnostics.Length);
        Assert.Contains(diagnostics,
            static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(value: "'UpdateValue'", StringComparison.Ordinal));
        Assert.Contains(diagnostics,
            static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(value: "'SetName'", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a declaration is reported when its name ends with a word its first parameter's name
    /// already says, for methods, async methods, extension methods, and local functions alike.
    /// </summary>
    /// <param name="members">The members declared in the type.</param>
    [Theory]
    [InlineData("public void SuckMyDick(Dick dick) { }")]
    [InlineData("public void UpdateValue(int value) { }")]
    [InlineData("public System.Threading.Tasks.Task UpdateValueAsync(int value) => System.Threading.Tasks.Task.CompletedTask;")]
    [InlineData("public void FromJob(string jobAbc) { }")]
    [InlineData("public void WithAnalyzers(int[] analyzers, int count) { }")]
    [InlineData("public bool ReadValue(out int value) { value = 0; return true; }")]
    [InlineData("public void Run() { void SetName(string name) { } SetName(name: \"entry\"); }")]
    public async Task ReportsRepeatedParameterNames(string members)
    {
        Assert.Equal(RedundantMethodSuffixAnalyzer.RuleIdentifier, Assert.Single(await Analyze(members)).Id);
    }

    private static async Task<Diagnostic[]> Analyze(string members)
    {
        return await AnalyzerTestHarness.Analyze(new RedundantMethodSuffixAnalyzer(), $"public sealed class Dick; public sealed class ExampleType {{ {members} }}")
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
