using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Naming;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Naming;

/// <summary>
/// Covers method names that end with a word their first argument already says.
/// </summary>
public sealed class RedundantMethodSuffixAnalyzerTests
{
    // Methods declared here can be renamed; Console and IFormattable belong to the framework.
    private const string Members = """
        using System;
        using System.Threading.Tasks;

        public sealed class Holder
        {
            public int Value { get; set; }
        }

        public static class HolderExtensions
        {
            public static void UpdateValue(this Holder holder, int value) { }
        }

        public sealed class ExampleType : IFormattable
        {
            public void UpdateValue(int value) { }
            public Task UpdateValueAsync(int value) => Task.CompletedTask;
            public void Update(int value) { }
            public void FromJob(string job) { }
            public void WithAnalyzers(int[] analyzers, int count) { }
            public void WithAnalyzer(int[] analyzers) { }
            public bool ReadValue(out int value) { value = 0; return true; }
            public void Log(string log) { }
            public string ToString(string? format, IFormatProvider? formatProvider) => string.Empty;

            public void Run(Holder options, int myValue, string jobAbc, int[] analyzers, int count)
            {
                BODY;
            }
        }
        """;

    /// <summary>
    /// Verifies a call is accepted when its method name says something its first argument does not, or
    /// when the method's name is not this project's to change.
    /// </summary>
    /// <param name="body">The statements calling the method.</param>
    [Theory]
    [InlineData("Update(value: 1)")]
    [InlineData("UpdateValue(count)")]
    [InlineData("WithAnalyzer(analyzers)")]
    [InlineData("Log(log: \"entry\")")]
    [InlineData("string line = jobAbc; Console.WriteLine(line)")]
    [InlineData("string formatString = jobAbc; ToString(formatString, formatProvider: null)")]
    public async Task AllowsDistinctOrForeignNames(string body)
    {
        Assert.Empty(await Analyze(body));
    }

    /// <summary>
    /// Verifies a call is reported when its method name ends with a word its first argument already says,
    /// whether that argument is named or read from a variable, property, or out declaration.
    /// </summary>
    /// <param name="body">The statements calling the method.</param>
    [Theory]
    [InlineData("UpdateValue(value: 1)")]
    [InlineData("UpdateValue(myValue)")]
    [InlineData("UpdateValue(options.Value)")]
    [InlineData("FromJob(jobAbc)")]
    [InlineData("WithAnalyzers(analyzers, count)")]
    [InlineData("ReadValue(out int value)")]
    [InlineData("UpdateValueAsync(value: 1)")]
    [InlineData("options.UpdateValue(myValue)")]
    [InlineData("options?.UpdateValue(myValue)")]
    [InlineData("void SetName(string name) { } SetName(name: \"entry\")")]
    public async Task ReportsRepeatedArgumentNames(string body)
    {
        Assert.Equal(RedundantMethodSuffixAnalyzer.RuleIdentifier, Assert.Single(await Analyze(body)).Id);
    }

    private static async Task<Diagnostic[]> Analyze(string body)
    {
        return await AnalyzerTestHarness.Analyze(new RedundantMethodSuffixAnalyzer(), Members.Replace(oldValue: "BODY", body, StringComparison.Ordinal))
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
