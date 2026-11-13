using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.StatementGroups;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.StatementGroups;

/// <summary>
/// Covers the limit of four statements per group, the comments that split longer groups, and the runs exempt
/// from it.
/// </summary>
public sealed class StatementGroupSizeTests
{
    private const string Type = "using System;\nusing System.Threading.Tasks;\n\npublic sealed class ExampleType\n{\n    private int _count;\n\n"
        + "    public event EventHandler? Changed;\n\n    public int Count { get; }\n\n    public string Name { get; } = string.Empty;\n\nMEMBERS\n}\n";

    /// <summary>
    /// Verifies groups of four or fewer are accepted, a comment line splits a longer run, and the exempt runs pass:
    /// argument checks, and a constructor storing one parameter per line. Statements written over several lines
    /// stand alone, so five of them in a row form no group.
    /// </summary>
    /// <param name="members">The members declared in the type.</param>
    [Theory]
    [InlineData("    public void Run() { int first = 1; int second = 2; int third = 3; int fourth = 4; }")]
    [InlineData("    public void Run()\n    {\n        int first = 1;\n        int second = 2;\n        int third = 3;\n\n        // The rest counts from the first.\n        int fourth = first + 3;\n        int fifth = first + 4;\n    }")]
    [InlineData("    public void Run(string a, string b, string c, string d, string e)\n    {\n        ArgumentNullException.ThrowIfNull(a);\n"
        + "        ArgumentNullException.ThrowIfNull(b);\n        ArgumentNullException.ThrowIfNull(c);\n        ArgumentNullException.ThrowIfNull(d);\n"
        + "        ArgumentNullException.ThrowIfNull(e);\n    }")]
    [InlineData("    public ExampleType(int count, string name, int total, int spare, EventHandler changed)\n    {\n        Count = count;\n"
        + "        Name = name;\n        _count = total;\n        _count = spare;\n        Changed = changed;\n    }")]
    [InlineData("    public void Run()\n    {\n        _count = 1\n            + 1;\n        _count = 2\n            + 2;\n        _count = 3\n            + 3;\n"
        + "        _count = 4\n            + 4;\n        _count = 5\n            + 5;\n    }")]
    public async Task AllowsShortAndExemptGroups(string members)
    {
        Assert.Empty(await Analyze(members));
    }

    /// <summary>
    /// Verifies the message names the size and kind of the group and tells the author how to split it.
    /// </summary>
    [Fact]
    public async Task ExplainsHowToSplitAGroup()
    {
        Diagnostic diagnostic = Assert.Single(await Analyze(members: "    public void Run() { int a = 1; int b = 2; int c = 3; int d = 4; int e = 5; }"));

        Assert.StartsWith(expectedStartString: "These 5 declarations run together as one group, and a group may hold at most 4 statements. Split it where its purpose changes",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        Assert.Contains(expectedSubstring: "A comment line starts a new group; a blank line does not",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a group of five is reported once, whatever kind it holds, when a blank line sits inside it, and
    /// when a constructor does more than store its parameters.
    /// </summary>
    /// <param name="members">The members declared in the type.</param>
    /// <param name="kind">The kind the message names.</param>
    [Theory]
    [InlineData("    public void Run()\n    {\n        int a = 1;\n        int b = 2;\n\n        int c = 3;\n        int d = 4;\n        int e = 5;\n    }",
        "declarations")]
    [InlineData("    public void Run() { _count = 1; _count += 2; _count++; --_count; _count = 0; }", "assignments")]
    [InlineData("    public void Run(Action action) { action(); action(); action(); action(); action(); }", "calls")]
    [InlineData("    public async Task Run(Task task) { await task; await task; await task; await task; await task; }", "await statements")]
    [InlineData("    public async Task Run(Task<int> task) { int a = await task; int b = await task; int c = await task; int d = await task; int e = await task; }",
        "declarations of awaited results")]
    [InlineData("    public async Task Run(Task<int> task) { _count = await task; _count = await task; _count += await task; _count = await task; _count = await task; }",
        "assignments of awaited results")]
    [InlineData("    public void Run(EventHandler handler) { Changed += handler; Changed -= handler; Changed += handler; Changed -= handler; Changed += handler; }",
        "event subscriptions")]
    [InlineData("    public ExampleType(int count, string name, int[] values)\n    {\n        Count = count;\n        Name = name;\n        _count = values.Length;\n"
        + "        _count = count;\n        _count = count;\n    }",
        "assignments")]
    [InlineData("    public void Store(int count, int total)\n    {\n        _count = count;\n        _count = total;\n        _count = count;\n        _count = total;\n"
        + "        _count = count;\n    }",
        "assignments")]
    public async Task ReportsGroupsOfFive(string members, string kind)
    {
        Diagnostic diagnostic = Assert.Single(await Analyze(members));

        Assert.Equal(StatementGroupSizeAnalyzer.RuleIdentifier, diagnostic.Id);
        Assert.StartsWith($"These 5 {kind} run together", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static async Task<Diagnostic[]> Analyze(string members)
    {
        return await AnalyzerTestHarness.Analyze(new StatementGroupSizeAnalyzer(), Type.Replace(oldValue: "MEMBERS", members, StringComparison.Ordinal))
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
