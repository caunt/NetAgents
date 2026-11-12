using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Documentation;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Documentation;

/// <summary>
/// Covers how property and constructor summaries open, and the fix that writes a constructor's opening.
/// </summary>
public sealed class SummaryOpeningTests
{
    /// <summary>
    /// Verifies openings matching the accessors a caller can use, and the standard constructor opening, are accepted.
    /// </summary>
    /// <param name="members">The members declared inside a public class.</param>
    [Theory]
    [InlineData("/// <summary>Gets the capacity.</summary>\n/// <value>How many fit.</value>\npublic int Capacity { get; }")]
    [InlineData("/// <summary>Gets or sets the capacity.</summary>\n/// <value>How many fit.</value>\npublic int Capacity { get; set; }")]
    [InlineData("/// <summary>Gets the capacity.</summary>\n/// <value>How many fit.</value>\npublic int Capacity { get; private set; }")]
    [InlineData("/// <summary>Gets or sets the capacity.</summary>\n/// <value>How many fit.</value>\npublic int Capacity { get; protected set; }")]
    [InlineData("/// <summary>Gets or initializes the capacity.</summary>\n/// <value>How many fit.</value>\npublic int Capacity { get; init; }")]
    [InlineData("/// <summary>Sets the capacity.</summary>\n/// <value>How many fit.</value>\npublic int Capacity { set { } }")]
    [InlineData("/// <summary>Initializes a new instance of the <see cref=\"ExampleType\"/> class.</summary>\npublic ExampleType() { }")]
    [InlineData("/// <summary>Initializes a new instance of the <see cref=\"ExampleType\"/> class with nothing queued.</summary>\npublic ExampleType() { }")]
    [InlineData("/// <summary>The capacity.</summary>\n/// <value>How many fit.</value>\ninternal int Capacity { get; }")]
    public async Task AcceptsStandardOpenings(string members)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new SummaryOpeningAnalyzer(), Wrap(members, type: "class")));
    }

    /// <summary>
    /// Verifies a generic type is named in the opening's cref with its type parameters in braces.
    /// </summary>
    [Fact]
    public async Task NamesGenericTypes()
    {
        const string source = """
            /// <summary>Holds frames of one kind.</summary>
            /// <typeparam name="TFrame">The kind of frame held.</typeparam>
            public class Queue<TFrame>
            {
                /// <summary>Creates an empty queue.</summary>
                public Queue() { }
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixSummaryOpenings(source, fixAll: true);

        Assert.Contains(expectedSubstring: "<summary>Initializes a new instance of the <see cref=\"Queue{TFrame}\"/> class. Creates an empty queue.</summary>",
            fixedSource,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the fix opens a constructor summary with the type and its kind, keeping what the author wrote
    /// after it, and replaces an opening written another way instead of adding a second one.
    /// </summary>
    /// <param name="type">The kind of type declared, as the source writes it.</param>
    /// <param name="summary">The constructor's summary as written.</param>
    /// <param name="expected">The constructor's summary after the fix.</param>
    [Theory]
    [InlineData("class",
        "Creates an empty queue.",
        "Initializes a new instance of the <see cref=\"ExampleType\"/> class. Creates an empty queue.")]
    [InlineData("struct",
        "Initializes a new instance of the ExampleType structure with nothing queued.",
        "Initializes a new instance of the <see cref=\"ExampleType\"/> struct with nothing queued.")]
    [InlineData("record",
        "Initializes a new instance of the <see cref=\"ExampleType\"/> class.",
        "Initializes a new instance of the <see cref=\"ExampleType\"/> record.")]
    public async Task OpensConstructorSummaries(string type, string summary, string expected)
    {
        const string constructor = "/// <summary>SUMMARY</summary>\npublic ExampleType() { }";

        string source = Wrap(constructor.Replace(oldValue: "SUMMARY", summary, StringComparison.Ordinal), type);
        string target = Wrap(constructor.Replace(oldValue: "SUMMARY", expected, StringComparison.Ordinal), type);

        Assert.Equal(target, await CodeFixTestHarness.FixSummaryOpenings(source, fixAll: true));
    }

    /// <summary>
    /// Verifies a property summary opening with a verb its visible accessors do not offer is reported with the one they do.
    /// </summary>
    /// <param name="members">The property as written.</param>
    /// <param name="expected">The opening the report asks for.</param>
    [Theory]
    [InlineData("/// <summary>The capacity.</summary>\n/// <value>How many fit.</value>\npublic int Capacity { get; }", "'Gets'")]
    [InlineData("/// <summary>Gets or sets the capacity.</summary>\n/// <value>How many fit.</value>\npublic int Capacity { get; private set; }",
        "'Gets'")]
    [InlineData("/// <summary>Gets the capacity.</summary>\n/// <value>How many fit.</value>\npublic int Capacity { get; set; }",
        "'Gets or sets'")]
    public async Task ReportsPropertyOpenings(string members, string expected)
    {
        Diagnostic diagnostic = Assert.Single(await AnalyzerTestHarness.Analyze(new SummaryOpeningAnalyzer(), Wrap(members, type: "class")));

        Assert.Contains(expected, diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static string Wrap(string members, string type)
    {
        return $$"""
            /// <summary>Holds the members under test.</summary>
            public {{type}} ExampleType
            {
            {{members}}
            }
            """;
    }
}
