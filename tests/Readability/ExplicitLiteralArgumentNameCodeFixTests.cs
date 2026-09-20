using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

/// <summary>
/// Covers the automatic fix that names literal arguments and drops unrequired names.
/// </summary>
public sealed class ExplicitLiteralArgumentNameCodeFixTests
{
    /// <summary>
    /// Verifies that a comment between expanded arguments blocks the collection rewrite.
    /// </summary>
    [Fact]
    public async Task KeepsCommentedVariadicArguments()
    {
        const string source = """
            class ExampleType
            {
                void Run() => Log(1, /* keep */ 2);
                void Log(params int[] values) { }
            }
            """;

        await CodeFixTestHarness.AssertNoLiteralArgumentNameFix(source);
    }

    /// <summary>
    /// Verifies that an argument list split by a preprocessor directive stays untouched.
    /// </summary>
    [Fact]
    public async Task KeepsDirectiveArguments()
    {
        const string source = """
            class ExampleType
            {
                void Run()
                {
                    Add(
            #if DEBUG
                        1,
            #else
                        2,
            #endif
                        3
                    );
                }

                void Add(int left, int right) { }
            }
            """;

        await CodeFixTestHarness.AssertNoLiteralArgumentNameFix(source);
    }

    /// <summary>
    /// Verifies that a variadic call keeps its literal arguments, because they have no parameter to name.
    /// </summary>
    [Fact]
    public async Task KeepsVarargArguments()
    {
        const string source = """
            class ExampleType
            {
                void Run() => Execute(1, __arglist(2));
                void Execute(int count, __arglist) { }
            }
            """;

        await CodeFixTestHarness.AssertNoLiteralArgumentNameFix(source);
    }

    /// <summary>
    /// Verifies that a parameter named after a keyword is escaped.
    /// </summary>
    [Fact]
    public async Task NamesKeywordParameters()
    {
        const string source = """
            class ExampleType
            {
                void Run() => Execute(1);
                void Execute(int @default) { }
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixLiteralArgumentNames(source, fixAll: true);

        Assert.Contains(expectedSubstring: "Execute(@default: 1)", fixedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the whole argument list converges in a single pass.
    /// </summary>
    /// <param name="invocation">The invocation to fix.</param>
    /// <param name="expected">The expected normalized invocation.</param>
    [Theory]
    [InlineData("Add(1, 2)", "Add(left: 1, right: 2)")]
    [InlineData("Add(-1, +2)", "Add(left: -1, right: +2)")]
    [InlineData("Add((1), -(2))", "Add(left: (1), right: -(2))")]
    [InlineData("Add(default, default)", "Add(left: default, right: default)")]
    [InlineData("Add(default(int), 2)", "Add(left: default(int), right: 2)")]
    [InlineData("Add(left, 2)", "Add(left, right: 2)")]
    public async Task NamesLiteralArguments(string invocation, string expected)
    {
        string source = $$"""
            class ExampleType
            {
                void Run(int left) => {{invocation}};
                void Add(int left, int right) { }
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixLiteralArgumentNames(source, fixAll: true);

        Assert.Equal(source.Replace(invocation, expected, StringComparison.Ordinal), fixedSource);
    }

    /// <summary>
    /// Verifies that constructors, base initializers, indexers, and delegates are all named.
    /// </summary>
    [Fact]
    public async Task NamesLiteralArgumentsInEveryCallShape()
    {
        const string source = """
            class Holder
            {
                public Holder(int number) { }

                public int this[int index] => index;
            }

            class Derived : Holder
            {
                public Derived() : base(3) { }

                public Derived(int other) : this() { }
            }

            delegate void Handler(int number);

            class ExampleType
            {
                void Run(Holder holder, Handler handler)
                {
                    Holder created = new Holder(3);
                    Holder targeted = new(3);
                    int value = holder[1];
                    handler(1);
                    System.Console.WriteLine(created);
                    System.Console.WriteLine(targeted);
                    System.Console.WriteLine(value);
                }
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixLiteralArgumentNames(source, fixAll: true);

        Assert.Contains(expectedSubstring: "base(number: 3)", fixedSource, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "new Holder(number: 3)", fixedSource, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "new(number: 3)", fixedSource, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "holder[index: 1]", fixedSource, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "handler(number: 1)", fixedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that nested argument lists are both normalized.
    /// </summary>
    [Fact]
    public async Task NamesNestedArgumentLists()
    {
        const string source = """
            class ExampleType
            {
                void Run() => Add(Inner(1), 2);
                int Inner(int number) => number;
                void Add(int left, int right) { }
            }
            """;

        const string expected = """
            class ExampleType
            {
                void Run() => Add(Inner(number: 1), right: 2);
                int Inner(int number) => number;
                void Add(int left, int right) { }
            }
            """;

        Assert.Equal(expected, await CodeFixTestHarness.FixLiteralArgumentNames(source, fixAll: true));
    }

    /// <summary>
    /// Verifies that expanded variadic arguments become a single named collection.
    /// </summary>
    /// <param name="invocation">The invocation to fix.</param>
    /// <param name="expected">The expected normalized invocation.</param>
    [Theory]
    [InlineData("Log(1, 2)", "Log(values: [1, 2])")]
    [InlineData("Log(1)", "Log(values: [1])")]
    [InlineData("Log(default)", "Log(values: default)")]
    public async Task NamesVariadicArgumentsAsCollection(string invocation, string expected)
    {
        string source = $$"""
            class ExampleType
            {
                void Run() => {{invocation}};
                void Log(params int[] values) { }
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixLiteralArgumentNames(source, fixAll: true);

        Assert.Equal(source.Replace(invocation, expected, StringComparison.Ordinal), fixedSource);
    }

    /// <summary>
    /// Verifies that expanded layout, comments, and line endings survive the fix.
    /// </summary>
    /// <param name="lineEnding">The document's existing line ending.</param>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task PreservesLayoutAndComments(string lineEnding)
    {
        string source = string.Join(
            lineEnding,
            value: ["class ExampleType", "{", "    void Run()", "    {", "        Add(", "            // the first operand", "            1,", "            2", "        );", "    }", "", "    void Add(int left, int right) { }", "}"]
        );

        string expected = source.Replace(oldValue: "            1,", newValue: "            left: 1,", StringComparison.Ordinal)
            .Replace(oldValue: "            2", newValue: "            right: 2", StringComparison.Ordinal);

        Assert.Equal(expected, await CodeFixTestHarness.FixLiteralArgumentNames(source, fixAll: true));
    }

    /// <summary>
    /// Verifies that names the rule does not require are removed.
    /// </summary>
    /// <param name="invocation">The invocation to fix.</param>
    /// <param name="expected">The expected normalized invocation.</param>
    [Theory]
    [InlineData("Add(left: left, right: right)", "Add(left, right)")]
    [InlineData("Add(left: 1, right: right)", "Add(left: 1, right)")]
    [InlineData("Add(left: left, right: 2)", "Add(left, right: 2)")]
    public async Task RemovesUnrequiredNames(string invocation, string expected)
    {
        string source = $$"""
            class ExampleType
            {
                void Run(int left, int right) => {{invocation}};
                void Add(int left, int right) { }
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixLiteralArgumentNames(source, fixAll: true);

        Assert.Equal(source.Replace(invocation, expected, StringComparison.Ordinal), fixedSource);
    }

    /// <summary>
    /// Verifies that a single registered fix normalizes the whole argument list it belongs to.
    /// </summary>
    [Fact]
    public async Task RewritesWholeListFromOneDiagnostic()
    {
        const string source = """
            class ExampleType
            {
                void Run() => Add(1, 2);
                void Add(int left, int right) { }
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixLiteralArgumentNames(source, fixAll: false);

        Assert.Contains(expectedSubstring: "Add(left: 1, right: 2)", fixedSource, StringComparison.Ordinal);
    }
}
