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
    /// Verifies groups of four or fewer are accepted, a comment line splits a longer run of statements or fields,
    /// documented members stand alone, and the exempt runs pass:
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
    [InlineData("    private int _first;\n    private int _second;\n    private int _third;\n\n    // Totals kept apart from the counters above.\n    private int _fourth;\n    private int _fifth;")]
    [InlineData("    /// <summary>The first.</summary>\n    public int First { get; set; }\n    /// <summary>The second.</summary>\n    public int Second { get; set; }\n"
        + "    /// <summary>The third.</summary>\n    public int Third { get; set; }\n    /// <summary>The fourth.</summary>\n    public int Fourth { get; set; }\n"
        + "    /// <summary>The fifth.</summary>\n    public int Fifth { get; set; }")]
    public async Task AllowsShortAndExemptGroups(string members)
    {
        Assert.Empty(await Analyze(members));
    }

    /// <summary>
    /// Verifies fields and properties that each store a primary constructor parameter, as it is or behind a <see langword="null"/>
    /// check, form a group of any length, while a run where one member computes its value is still limited.
    /// </summary>
    /// <param name="members">The members of a type whose primary constructor takes a through e.</param>
    /// <param name="reported">Whether the run is reported.</param>
    [Theory]
    [InlineData("    private readonly string _a = a ?? throw new ArgumentNullException(nameof(a));\n    private readonly string _b = b;\n    private readonly string _c = c;\n"
        + "    private readonly string _d = d;\n    private readonly string _e = e ?? throw new ArgumentNullException(nameof(e));",
        "false")]
    [InlineData("    private readonly string _a = a;\n    private readonly string _b = b;\n    private readonly string _c = c;\n    private readonly string _d = d;\n"
        + "    private readonly string _e = e.Trim();",
        "true")]
    public async Task ExemptsStoredConstructorParameters(string members, string reported)
    {
        string source = $"using System;\n\npublic sealed class ExampleType(string a, string b, string c, string d, string e)\n{{\n{members}\n\n    public string All => _a + _b + _c + _d + _e;\n}}\n";

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new StatementGroupSizeAnalyzer(), source);

        Assert.Equal(bool.Parse(reported), diagnostics.Length == 1);
    }

    /// <summary>
    /// Verifies the message names the size and kind of the group and tells the author how to split it.
    /// </summary>
    [Fact]
    public async Task ExplainsHowToSplitAGroup()
    {
        Diagnostic diagnostic = Assert.Single(await Analyze(members: "    public void Run() { int a = 1; int b = 2; int c = 3; int d = 4; int e = 5; }"));

        Assert.StartsWith(expectedStartString: "These 5 declarations run together as one group, and a group may hold at most 4. Split it where its purpose changes",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        Assert.Contains(expectedSubstring: "A comment line starts a new group; a blank line does not",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a group of five is reported once, whatever kind of statement or member it holds, when a blank line
    /// sits inside it, and when a constructor does more than store its parameters.
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
    [InlineData("    private int _first;\n    private int _second;\n    private int _third;\n    private int _fourth;\n    private int _fifth;",
        "fields")]
    [InlineData("    private int First { get; set; }\n    private int Second { get; set; }\n    private int Third { get; set; }\n    private int Fourth { get; set; }\n"
        + "    private int Fifth { get; set; }",
        "properties")]
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
