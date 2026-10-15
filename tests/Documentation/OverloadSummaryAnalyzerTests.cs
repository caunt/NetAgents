using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Documentation;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Documentation;

/// <summary>
/// Covers overloads whose summaries leave a reader unable to choose between them.
/// </summary>
public sealed class OverloadSummaryAnalyzerTests
{
    /// <summary>
    /// Verifies that two types may describe their own same-named members identically.
    /// </summary>
    [Fact]
    public async Task AcceptsMatchingSummariesInDifferentTypes()
    {
        const string source = """
            /// <summary>Resolves data tables eagerly.</summary>
            public static class EagerExampleType
            {
                /// <summary>Resolves every table found under the asset directory.</summary>
                /// <returns>The resolved tables.</returns>
                public static string Resolve() => string.Empty;
            }

            /// <summary>Resolves data tables lazily.</summary>
            public static class LazyExampleType
            {
                /// <summary>Resolves every table found under the asset directory.</summary>
                /// <returns>The resolved tables.</returns>
                public static string Resolve() => string.Empty;
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new OverloadSummaryAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that overloads saying what separates them are accepted.
    /// </summary>
    [Fact]
    public async Task AcceptsOverloadsDescribingTheirDifference()
    {
        const string source = """
            /// <summary>Resolves data tables.</summary>
            public static class ExampleType
            {
                /// <summary>Resolves every table found under the asset directory.</summary>
                /// <returns>The resolved tables.</returns>
                public static string Resolve() => string.Empty;

                /// <summary>Resolves only the table the caller names.</summary>
                /// <param name="table">The table to resolve.</param>
                /// <returns>The resolved table.</returns>
                public static string Resolve(string table) => table;
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new OverloadSummaryAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that a repeated summary is reported on the later overload, once per repetition.
    /// </summary>
    [Fact]
    public async Task RejectsOverloadsSharingASummary()
    {
        const string source = """
            /// <summary>Resolves data tables.</summary>
            public static class ExampleType
            {
                /// <summary>Resolves every table found under the asset directory.</summary>
                /// <returns>The resolved tables.</returns>
                public static string Resolve() => string.Empty;

                /// <summary>Resolves every table found under the asset directory.</summary>
                /// <param name="table">The table to resolve.</param>
                /// <returns>The resolved tables.</returns>
                public static string Resolve(string table) => table;
            }
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new OverloadSummaryAnalyzer(), source);

        Assert.Equal(OverloadSummaryAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
        Assert.Equal(expected: 10, diagnostics[0].Location.GetLineSpan().StartLinePosition.Line);
    }

    /// <summary>
    /// Verifies that wrapping a summary across lines does not make it a different sentence.
    /// </summary>
    [Fact]
    public async Task RejectsRepeatedSummariesWrappedDifferently()
    {
        const string source = """
            /// <summary>Resolves data tables.</summary>
            public static class ExampleType
            {
                /// <summary>Resolves every table found under the asset directory.</summary>
                /// <returns>The resolved tables.</returns>
                public static string Resolve() => string.Empty;

                /// <summary>
                /// Resolves every table found under the asset directory.
                /// </summary>
                /// <param name="table">The table to resolve.</param>
                /// <returns>The resolved tables.</returns>
                public static string Resolve(string table) => table;
            }
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new OverloadSummaryAnalyzer(), source);

        Assert.Equal(OverloadSummaryAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }
}
