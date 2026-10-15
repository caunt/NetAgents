using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Documentation;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Documentation;

/// <summary>
/// Covers documentation prose written outside every element, where no reader ever sees it.
/// </summary>
public sealed class DocumentationProseAnalyzerTests
{
    /// <summary>
    /// Verifies that an inherited comment may carry a note, because it already points somewhere.
    /// </summary>
    [Fact]
    public async Task AcceptsNotesBesideInheritedDocumentation()
    {
        const string source = """
            /// <summary>Reads one record at a time.</summary>
            public interface IExampleContract
            {
                /// <summary>Reads the next record, or null once the source is spent.</summary>
                string? Read();
            }

            /// <summary>Reads records from a fixed list.</summary>
            public sealed class ExampleType : IExampleContract
            {
                /// <inheritdoc />
                /// Always returns null until the list is attached.
                public string? Read() => null;
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new DocumentationProseAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that a comment whose words all sit inside elements is accepted.
    /// </summary>
    [Fact]
    public async Task AcceptsProseInsideElements()
    {
        const string source = """
            /// <summary>Holds one greeting.</summary>
            public static class ExampleType
            {
                /// <summary>Builds the greeting shown on the welcome screen.</summary>
                /// <param name="recipientName">The name shown in the greeting.</param>
                /// <returns>The greeting text.</returns>
                /// <remarks>Trims the name first.</remarks>
                public static string Greet(string recipientName) => recipientName.Trim();
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new DocumentationProseAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that prose outside every element is rejected wherever it sits in the comment.
    /// </summary>
    /// <param name="comment">A documentation comment carrying prose no element encloses.</param>
    [Theory]
    [InlineData("/// Gets the retained birth timestamp used by age restrictions.")]
    [InlineData("/// Gets the country code.\n    /// <remarks>Used by age restrictions.</remarks>")]
    [InlineData("/// <remarks>Used by age restrictions.</remarks>\n    /// Gets the country code.")]
    public async Task RejectsProseOutsideElements(string comment)
    {
        string source = $$"""
            /// <summary>Holds one avatar.</summary>
            public sealed class ExampleType
            {
                {{comment}}
                public string CountryCode { get; } = string.Empty;
            }
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new DocumentationProseAnalyzer(), source);

        Assert.Equal(DocumentationProseAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Verifies that the fix moves the prose into a summary and leaves nothing to report.
    /// </summary>
    [Fact]
    public async Task WrapsLooseProseInASummary()
    {
        const string source = """
            /// <summary>Holds one avatar.</summary>
            public sealed class ExampleType
            {
                /// Gets the country code used by age restrictions.
                public string CountryCode { get; } = string.Empty;
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixDocumentationProse(source);

        Assert.Contains(
            expectedSubstring: "/// <summary>Gets the country code used by age restrictions.</summary>",
            fixedSource,
            StringComparison.Ordinal
        );

        Assert.Empty(await AnalyzerTestHarness.Analyze(new DocumentationProseAnalyzer(), fixedSource));
    }
}
