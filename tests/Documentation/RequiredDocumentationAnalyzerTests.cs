using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Documentation;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Documentation;

/// <summary>
/// Covers the tags a public member's signature calls for, and the members whose tags are left to their authors.
/// </summary>
public sealed class RequiredDocumentationAnalyzerTests
{
    /// <summary>
    /// Verifies a member carrying every tag its signature needs is accepted, as are members outside the
    /// public surface, inherited comments, and exceptions the member catches, rethrows, or leaves to a lambda.
    /// </summary>
    /// <param name="members">The members declared inside a public class.</param>
    [Theory]
    [InlineData("""
        /// <summary>Adds a frame at a position.</summary>
        /// <typeparam name="TFrame">The kind of frame stored.</typeparam>
        /// <param name="frame">The frame to store.</param>
        /// <param name="position">Where the frame goes.</param>
        /// <returns>The position it went to.</returns>
        /// <exception cref="ArgumentNullException">Thrown when no frame is given.</exception>
        public int Add<TFrame>(TFrame frame, int position) { ArgumentNullException.ThrowIfNull(frame); return position; }
        """)]
    [InlineData("/// <summary>Gets the capacity.</summary>\n/// <value>How many frames fit.</value>\npublic int Capacity { get; }")]
    [InlineData("/// <summary>Sends everything queued.</summary>\npublic Task SendAsync() => Task.CompletedTask;")]
    [InlineData("/// <summary>Clears the queue.</summary>\npublic void Clear() { }")]
    [InlineData("/// <summary>Adds a frame.</summary>\ninternal int Add(int frame) => frame;")]
    [InlineData("/// <summary>Adds a frame.</summary>\nprivate int Add(int frame) => frame;")]
    [InlineData("/// <inheritdoc/>\npublic override string ToString() => string.Empty;")]
    [InlineData("/// <summary>Reads once.</summary>\npublic void Read() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) { } }")]
    [InlineData("/// <summary>Reads once.</summary>\npublic void Read() { try { Clear(); } catch { throw; } }\n/// <summary>Clears the queue.</summary>\npublic void Clear() { }")]
    [InlineData("/// <summary>Builds a callback.</summary>\n/// <returns>A callback that fails.</returns>\npublic Action Build() => () => throw new InvalidOperationException();")]
    [InlineData("/// <summary>Checks a name.</summary>\n/// <param name=\"name\">The name checked.</param>\n/// <exception cref=\"ArgumentException\">Thrown for a missing name.</exception>\npublic void Check(string name) => ArgumentNullException.ThrowIfNull(name);")]
    [InlineData("/// <summary>A frame on the wire.</summary>\n/// <param name=\"Name\">The frame's label.</param>\npublic record Frame(string Name);")]
    public async Task AcceptsCompleteDocumentation(string members)
    {
        Assert.Empty(await Analyze(members));
    }

    /// <summary>
    /// Verifies each missing tag is reported, for methods, constructors, indexers, delegates, records,
    /// properties, and the exceptions a body throws directly or through a throw helper.
    /// </summary>
    /// <param name="members">The members declared inside a public class.</param>
    /// <param name="expected">Text the report uses to name what is missing.</param>
    [Theory]
    [InlineData("/// <summary>Adds a frame.</summary>\npublic void Add(int frame) { }", "parameter 'frame'")]
    [InlineData("/// <summary>Maps an item.</summary>\n/// <param name=\"item\">The item mapped.</param>\n/// <returns>The item as given.</returns>\npublic TResult Map<TResult>(TResult item) => item;",
        "type parameter 'TResult'")]
    [InlineData("/// <summary>Sends a frame.</summary>\n/// <param name=\"frame\">What is sent.</param>\npublic Task<int> SendAsync(string frame) => Task.FromResult(frame.Length);",
        "<returns>")]
    [InlineData("/// <summary>Gets the capacity.</summary>\npublic int Capacity { get; }", "<value>")]
    [InlineData("/// <summary>Gets a frame.</summary>\n/// <value>The frame found there.</value>\npublic int this[int position] => position;",
        "parameter 'position'")]
    [InlineData("/// <summary>Initializes a new instance of the <see cref=\"ExampleType\"/> class.</summary>\npublic ExampleType(int capacity) { }",
        "parameter 'capacity'")]
    [InlineData("/// <summary>Calls back with a frame.</summary>\npublic delegate void FrameCallback(int frame);", "parameter 'frame'")]
    [InlineData("/// <summary>A frame on the wire.</summary>\npublic record Frame(string Name);", "parameter 'Name'")]
    [InlineData("/// <summary>Grows the queue.</summary>\n/// <param name=\"count\">How many slots to add.</param>\npublic void Grow(int count) => ArgumentOutOfRangeException.ThrowIfNegative(count);",
        "'ArgumentOutOfRangeException'")]
    [InlineData("/// <summary>Stops the queue.</summary>\npublic void Stop() => throw new InvalidOperationException();",
        "'InvalidOperationException'")]
    [InlineData("/// <summary>Gets the frame.</summary>\n/// <value>The frame held.</value>\npublic int Frame => throw new NotSupportedException();",
        "'NotSupportedException'")]
    public async Task ReportsMissingTags(string members, string expected)
    {
        Diagnostic diagnostic = Assert.Single(await Analyze(members));

        Assert.Equal(RequiredDocumentationAnalyzer.RuleIdentifier, diagnostic.Id);
        Assert.Contains(expected, diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static async Task<Diagnostic[]> Analyze(string members)
    {
        string source = $$"""
            using System;
            using System.Threading.Tasks;

            /// <summary>Holds the members under test.</summary>
            public class ExampleType
            {
            {{members}}
            }
            """;

        return await AnalyzerTestHarness.Analyze(new RequiredDocumentationAnalyzer(), source).ConfigureAwait(continueOnCapturedContext: false);
    }
}
