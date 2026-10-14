using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Documentation;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Documentation;

/// <summary>
/// Covers documentation comments that describe their declaration more than once.
/// </summary>
public sealed class SummaryUniquenessAnalyzerTests
{
    /// <summary>
    /// Verifies that a comment pairing one summary with other elements is accepted.
    /// </summary>
    [Fact]
    public async Task AllowsOneSummaryBesideOtherElements()
    {
        const string source = """
            /// <summary>Greets a recipient.</summary>
            /// <param name="recipientName">The name to greet.</param>
            /// <returns>The greeting.</returns>
            /// <remarks>Also describes the greeting.</remarks>
            public static class ExampleType
            {
                /// <summary>Greets the recipient.</summary>
                /// <param name="recipientName">The name to greet.</param>
                /// <returns>The greeting.</returns>
                public static string Greet(string recipientName) => recipientName;
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new SummaryUniquenessAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that separate declarations each keep their own summary.
    /// </summary>
    [Fact]
    public async Task AllowsOneSummaryPerDeclaration()
    {
        const string source = """
            /// <summary>Describes the type.</summary>
            public static class ExampleType
            {
                /// <summary>Describes the first member.</summary>
                public static int First => 1;

                /// <summary>Describes the second member.</summary>
                public static int Second => 2;
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new SummaryUniquenessAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that every summary after the first is reported, however many are stacked.
    /// </summary>
    [Fact]
    public async Task ReportsEverySummaryAfterTheFirst()
    {
        const int extra = 9;
        string stacked = string.Concat(Enumerable.Range(start: 0, extra).Select(static index => $"/// <summary>Describes member {index}.</summary>\n"));
        string source = $"/// <summary>Describes the type.</summary>\n{stacked}public sealed record ExampleType(int Value);\n";

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new SummaryUniquenessAnalyzer(), source);

        Assert.Equal(extra, diagnostics.Length);
        Assert.All(diagnostics, static diagnostic => Assert.Equal(SummaryUniquenessAnalyzer.RuleIdentifier, diagnostic.Id));
    }

    /// <summary>
    /// Verifies that a stacked summary is reported where it is written, not on the declaration.
    /// </summary>
    [Fact]
    public async Task ReportsTheDuplicateSummaryItself()
    {
        const string source = """
            /// <summary>Describes the type.</summary>
            /// <summary>Describes the value.</summary>
            public sealed record ExampleType(int Value);
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new SummaryUniquenessAnalyzer(), source);
        Diagnostic diagnostic = Assert.Single(diagnostics);

        Assert.Equal(expected: 1, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }
}
