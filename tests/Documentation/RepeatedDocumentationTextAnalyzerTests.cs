using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Documentation;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Documentation;

/// <summary>
/// Covers the tags of one member repeating each other's words.
/// </summary>
public sealed class RepeatedDocumentationTextAnalyzerTests
{
    /// <summary>
    /// Verifies tags saying different things are accepted, as is a repeat on a member outside the public surface.
    /// </summary>
    /// <param name="members">The members declared inside a public class.</param>
    [Theory]
    [InlineData("/// <summary>Raises the lamp.</summary>\n/// <param name=\"step\">How far to raise it.</param>\n/// <param name=\"cap\">The brightest it may get.</param>\npublic void Raise(int step, int cap) { }")]
    [InlineData("/// <summary>Raises the lamp.</summary>\n/// <param name=\"step\">Step to raise by.</param>\n/// <param name=\"cap\">Step to raise by.</param>\ninternal void Raise(int step, int cap) { }")]
    public async Task AcceptsDistinctText(string members)
    {
        Assert.Empty(await Analyze(members));
    }

    /// <summary>
    /// Verifies a tag repeating another's text is reported once, however it is cased, wrapped, or punctuated.
    /// </summary>
    /// <param name="members">The members declared inside a public class.</param>
    [Theory]
    [InlineData("/// <summary>Raises the lamp.</summary>\n/// <param name=\"step\">Step to raise by.</param>\n/// <param name=\"cap\">Step to raise by.</param>\npublic void Raise(int step, int cap) { }")]
    [InlineData("/// <summary>Raises the lamp.</summary>\n/// <param name=\"step\">Step to raise by.</param>\n/// <param name=\"cap\">\n/// step to raise by\n/// </param>\npublic void Raise(int step, int cap) { }")]
    [InlineData("/// <summary>Reads the count.</summary>\n/// <returns>Reads the count.</returns>\npublic int Read() => 0;")]
    public async Task ReportsRepeatedText(string members)
    {
        Assert.Equal(RepeatedDocumentationTextAnalyzer.RuleIdentifier, Assert.Single(await Analyze(members)).Id);
    }

    private static async Task<Diagnostic[]> Analyze(string members)
    {
        string source = $$"""
            /// <summary>Holds the members under test.</summary>
            public class ExampleType
            {
            {{members}}
            }
            """;

        return await AnalyzerTestHarness.Analyze(new RepeatedDocumentationTextAnalyzer(), source).ConfigureAwait(continueOnCapturedContext: false);
    }
}
