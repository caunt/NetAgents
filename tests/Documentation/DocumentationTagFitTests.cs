using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Documentation;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Documentation;

/// <summary>
/// Covers tags that do not belong to the member they sit on, and the fixes that remove or reorder them.
/// </summary>
public sealed class DocumentationTagFitTests
{
    /// <summary>
    /// Verifies tags that fit are accepted: a result on a method returning one, parameters in declaration
    /// order, an inherited comment with something to inherit, and anything on a member outside the public surface.
    /// </summary>
    /// <param name="members">The members declared inside a public class.</param>
    [Theory]
    [InlineData("/// <summary>Reads the count.</summary>\n/// <returns>How many were read.</returns>\npublic int Read() => 0;")]
    [InlineData("/// <summary>Reads a record.</summary>\n/// <param name=\"name\">Which record.</param>\n/// <param name=\"offset\">Where to start.</param>\npublic void Read(string name, int offset) { }")]
    [InlineData("/// <inheritdoc/>\npublic override string ToString() => string.Empty;")]
    [InlineData("/// <summary>Runs work.</summary>\npublic interface IRunner\n{\n/// <summary>Runs once.</summary>\nvoid Run();\n}\n/// <summary>Runs nothing.</summary>\npublic sealed class Runner : IRunner\n{\n/// <inheritdoc/>\npublic void Run() { }\n}")]
    [InlineData("/// <inheritdoc cref=\"object.ToString\"/>\npublic string Describe() => string.Empty;")]
    [InlineData("/// <summary>Forgets the queue.</summary>\n/// <returns>Nothing at all.</returns>\ninternal void Forget() { }")]
    public async Task AcceptsFittingTags(string members)
    {
        Assert.Empty(await Analyze(members));
    }

    /// <summary>
    /// Verifies the fix removes a tag the member has no use for, together with the comment line it occupied,
    /// and puts parameter tags in declaration order.
    /// </summary>
    /// <param name="members">The members as written.</param>
    /// <param name="expected">The members after the fix.</param>
    [Theory]
    [InlineData("/// <summary>Forgets the queue.</summary>\n/// <returns>Nothing at all.</returns>\npublic void Forget() { }",
        "/// <summary>Forgets the queue.</summary>\npublic void Forget() { }")]
    [InlineData("/// <summary>Sends the queue.</summary>\n/// <returns>The work underway.</returns>\npublic Task SendAsync() => Task.CompletedTask;",
        "/// <summary>Sends the queue.</summary>\npublic Task SendAsync() => Task.CompletedTask;")]
    [InlineData("/// <summary>Initializes a new instance of the <see cref=\"ExampleType\"/> class.</summary>\n/// <returns>The instance made.</returns>\npublic ExampleType() { }",
        "/// <summary>Initializes a new instance of the <see cref=\"ExampleType\"/> class.</summary>\npublic ExampleType() { }")]
    [InlineData("/// <summary>Forgets the queue.</summary>\n/// <value>What is forgotten.</value>\npublic void Forget() { }",
        "/// <summary>Forgets the queue.</summary>\npublic void Forget() { }")]
    [InlineData("/// <summary>Sets the current.</summary>\n/// <param name=\"amps\"><placeholder>TBD</placeholder>The current drawn.</param>\npublic void Draw(int amps) { }",
        "/// <summary>Sets the current.</summary>\n/// <param name=\"amps\">The current drawn.</param>\npublic void Draw(int amps) { }")]
    [InlineData("/// <summary>Reads a record.</summary>\n/// <param name=\"offset\">Where to start.</param>\n/// <param name=\"name\">Which record.</param>\npublic void Read(string name, int offset) { }",
        "/// <summary>Reads a record.</summary>\n/// <param name=\"name\">Which record.</param>\n/// <param name=\"offset\">Where to start.</param>\npublic void Read(string name, int offset) { }")]
    [InlineData("/// <summary>Maps a pair.</summary>\n/// <typeparam name=\"TValue\">What is produced.</typeparam>\n/// <typeparam name=\"TKey\">What is looked up.</typeparam>\npublic void Map<TKey, TValue>() { }",
        "/// <summary>Maps a pair.</summary>\n/// <typeparam name=\"TKey\">What is looked up.</typeparam>\n/// <typeparam name=\"TValue\">What is produced.</typeparam>\npublic void Map<TKey, TValue>() { }")]
    public async Task FixesTagsThatDoNotFit(string members, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixDocumentationTagFit(Wrap(members), fixAll: true));
    }

    /// <summary>
    /// Verifies an inherited comment on a member that overrides and implements nothing is reported, with no
    /// fix, since the missing text is the author's to write.
    /// </summary>
    [Fact]
    public async Task ReportsInheritingNothing()
    {
        Diagnostic diagnostic = Assert.Single(await Analyze(members: "/// <inheritdoc/>\npublic void Forget() { }"));

        Assert.Contains(expectedSubstring: "nothing to inherit", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(diagnostic.Properties);
    }

    private static async Task<Diagnostic[]> Analyze(string members)
    {
        return await AnalyzerTestHarness.Analyze(new DocumentationTagFitAnalyzer(), Wrap(members)).ConfigureAwait(continueOnCapturedContext: false);
    }

    private static string Wrap(string members)
    {
        return $$"""
            using System.Threading.Tasks;

            /// <summary>Holds the members under test.</summary>
            public class ExampleType
            {
            {{members}}
            }
            """;
    }
}
