using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Documentation;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Documentation;

/// <summary>
/// Covers documentation text that must end its sentences, and the fix that adds the missing full stop.
/// </summary>
public sealed class DocumentationSentenceTests
{
    /// <summary>
    /// Verifies text already closed by a full stop, a question, or an exclamation is accepted, as are text
    /// ending on a list or a code block, a closing paragraph that ends its sentence, and members outside the
    /// public surface.
    /// </summary>
    /// <param name="members">The members declared inside a public class.</param>
    [Theory]
    [InlineData("/// <summary>Raises the lamp.</summary>\npublic void Raise() { }")]
    [InlineData("/// <summary>Is the lamp lit?</summary>\npublic void Check() { }")]
    [InlineData("/// <summary>\n/// Raises the lamp.\n/// </summary>\npublic void Raise() { }")]
    [InlineData("/// <summary>Raises the lamp:\n/// <list type=\"bullet\"><item>by one step</item></list>\n/// </summary>\npublic void Raise() { }")]
    [InlineData("/// <summary>Raises the lamp.<para>Then it stays lit.</para></summary>\npublic void Raise() { }")]
    [InlineData("/// <summary>Raises the lamp</summary>\ninternal void Raise() { }")]
    public async Task AcceptsEndedSentences(string members)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new DocumentationSentenceAnalyzer(), Wrap(members)));
    }

    /// <summary>
    /// Verifies the fix ends the sentence where its words or its last inline element stop, inside a wrapped
    /// comment and inside a closing paragraph alike.
    /// </summary>
    /// <param name="members">The members as written.</param>
    /// <param name="expected">The members after the fix.</param>
    [Theory]
    [InlineData("/// <summary>A lamp whose brightness callers raise and lower</summary>\npublic void Raise() { }",
        "/// <summary>A lamp whose brightness callers raise and lower.</summary>\npublic void Raise() { }")]
    [InlineData("/// <summary>\n/// Raises the lamp\n/// </summary>\npublic void Raise() { }",
        "/// <summary>\n/// Raises the lamp.\n/// </summary>\npublic void Raise() { }")]
    [InlineData("/// <summary>Raises the <see cref=\"ExampleType\"/></summary>\npublic void Raise() { }",
        "/// <summary>Raises the <see cref=\"ExampleType\"/>.</summary>\npublic void Raise() { }")]
    [InlineData("/// <summary>Raises the lamp.<para>Then it stays lit</para></summary>\npublic void Raise() { }",
        "/// <summary>Raises the lamp.<para>Then it stays lit.</para></summary>\npublic void Raise() { }")]
    [InlineData("/// <summary>Raises the lamp.</summary>\n/// <param name=\"step\">How far to raise it</param>\npublic void Raise(int step) { }",
        "/// <summary>Raises the lamp.</summary>\n/// <param name=\"step\">How far to raise it.</param>\npublic void Raise(int step) { }")]
    public async Task EndsSentences(string members, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixDocumentationSentences(Wrap(members), fixAll: true));
    }

    /// <summary>
    /// Verifies each unfinished element is reported on its own, so a summary and a parameter both lacking a
    /// full stop produce two reports.
    /// </summary>
    [Fact]
    public async Task ReportsEachElement()
    {
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new DocumentationSentenceAnalyzer(),
            Wrap(members: "/// <summary>Raises the lamp</summary>\n/// <param name=\"step\">How far to raise it</param>\npublic void Raise(int step) { }"));

        Assert.Equal(expected: 2, diagnostics.Length);
    }

    private static string Wrap(string members)
    {
        return $$"""
            /// <summary>Holds the members under test.</summary>
            public class ExampleType
            {
            {{members}}
            }
            """;
    }
}
