using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Documentation;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Documentation;

/// <summary>
/// Covers documentation elements measured against the name they describe.
/// </summary>
public sealed class DocumentationInformationAnalyzerTests
{
    /// <summary>
    /// Verifies that an inherited comment is never measured, because its text lives on the base.
    /// </summary>
    [Fact]
    public async Task AcceptsInheritedDocumentation()
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
                public string? Read() => null;
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new DocumentationInformationAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that one unfamiliar word is enough, so the filler list stays free of domain terms.
    /// </summary>
    [Fact]
    public async Task AcceptsSummariesCarryingOnlyDomainVocabulary()
    {
        const string source = """
            /// <summary>Carries one neighborhood.</summary>
            public sealed class ExampleType
            {
                /// <summary>Gets the neighborhood name shown on the roadside signpost.</summary>
                public string NeighborhoodName { get; } = string.Empty;
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new DocumentationInformationAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that a summary adding any word outside the name is accepted.
    /// </summary>
    /// <param name="summary">A summary that says something the name does not.</param>
    [Theory]
    [InlineData("Gets or sets the absolute consumer project path.")]
    [InlineData("Gets the path this build writes its output to.")]
    [InlineData("Gets the project path, resolved against the repository root.")]
    public async Task AcceptsSummariesThatSaySomething(string summary)
    {
        string source = $$"""
            /// <summary>Holds one build's inputs.</summary>
            public sealed class ExampleType
            {
                /// <summary>{{summary}}</summary>
                public string ProjectPath { get; set; } = string.Empty;
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new DocumentationInformationAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that a parameter description repeating only its own name is rejected.
    /// </summary>
    [Fact]
    public async Task RejectsParametersThatRestateTheirName()
    {
        const string source = """
            /// <summary>Greets one recipient by name.</summary>
            public static class ExampleType
            {
                /// <summary>Builds the greeting shown on the welcome screen.</summary>
                /// <param name="recipientName">The recipient name.</param>
                /// <returns>The greeting.</returns>
                public static string Greet(string recipientName) => recipientName;
            }
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new DocumentationInformationAnalyzer(), source);

        Assert.Equal(DocumentationInformationAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
        Assert.Contains(expectedSubstring: "recipientName", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a summary saying only what the name says is rejected, however it is spelled.
    /// </summary>
    /// <param name="summary">A summary carrying no word the member name lacks.</param>
    [Theory]
    [InlineData("Gets the Changes value.")]
    [InlineData("Provides the Changes value or operation.")]
    [InlineData("Represents <c language=\"csharp\">Changes</c>.")]
    [InlineData("The changes.")]
    [InlineData("")]
    public async Task RejectsSummariesThatRestateTheName(string summary)
    {
        string source = $$"""
            /// <summary>Records one edit to a data table.</summary>
            public sealed class ExampleType
            {
                /// <summary>{{summary}}</summary>
                public string Changes { get; } = string.Empty;
            }
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new DocumentationInformationAnalyzer(), source);

        Assert.Equal(DocumentationInformationAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Verifies that the rule reaches types and their type parameters, not only their members.
    /// </summary>
    [Fact]
    public async Task RejectsTypesAndTypeParametersThatRestateTheirName()
    {
        const string source = """
            /// <summary>Defines the Example Type contract.</summary>
            /// <typeparam name="TElement">The element.</typeparam>
            public sealed class ExampleType<TElement>
            {
                /// <summary>Counts the elements held right now.</summary>
                public int Count { get; }
            }
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new DocumentationInformationAnalyzer(), source);

        Assert.Equal(expected: 2, diagnostics.Length);
    }
}
