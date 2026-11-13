using System.Text.RegularExpressions;

using NetAgents.Analyzers.StatementGroups;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.StatementGroups;

/// <summary>
/// Covers the blank lines that separate groups of statements, and the fix that moves them.
/// </summary>
public sealed class StatementGroupingTests
{
    private const string Method = "using System;\nusing System.Threading.Tasks;\n\npublic sealed class ExampleType\n{\n    private int _count;\n\n"
        + "    public event EventHandler? Changed;\n\n    public async Task Execute(string name, Task pending, Task<string> reading, Action action)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies grouped statements are accepted, and that the pairs NETAGENTS0016 separates, statements behind a
    /// preprocessor directive, and blank lines inside a block comment are left to their own rules.
    /// </summary>
    /// <param name="statements">The statements of the method, one per line.</param>
    [Theory]
    [InlineData("int first = 1;\nint second = 2;\n\nConsole.WriteLine(first + second);")]
    [InlineData("Console.WriteLine(name);\nif (name.Length > 0)\n    return;")]
    [InlineData("int first = 1;\n\n// Show the total before saving it.\nConsole.WriteLine(first);")]
    [InlineData("int first = 1;\n#if DEBUG\nint second = 2;\n#endif\nConsole.WriteLine(first);")]
    [InlineData("int first = 1;\n\n/* The second value.\n\n   It doubles the first. */\nint second = first * 2;")]
    public async Task AcceptsGroupedCode(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new StatementGroupingAnalyzer(), Wrap(statements)));
    }

    /// <summary>
    /// Verifies the groups of every kind of body: an accessor, a lambda, and a switch section, where the blank line
    /// a <c>break</c> needs is left to NETAGENTS0016.
    /// </summary>
    [Fact]
    public async Task RegroupsEveryKindOfBody()
    {
        const string source = """
            using System;

            public sealed class ExampleType
            {
                private int _count;
                private string _name = string.Empty;

                public string Name
                {
                    get => _name;
                    set
                    {
                        _name = value;
                        _count++;
                        Console.WriteLine(value);
                    }
                }

                public Action Create()
                {
                    return () =>
                    {
                        int next = _count + 1;
                        _count = next;
                    };
                }

                public void Pick(int choice)
                {
                    switch (choice)
                    {
                        case 1:
                            _count = 1;
                            Console.WriteLine(choice);
                            break;
                        default:
                            break;
                    }
                }
            }
            """;

        string expected = source
            .Replace(oldValue: "_count++;\n", newValue: "_count++;\n\n", StringComparison.Ordinal)
            .Replace(oldValue: "int next = _count + 1;\n", newValue: "int next = _count + 1;\n\n", StringComparison.Ordinal)
            .Replace(oldValue: "_count = 1;\n", newValue: "_count = 1;\n\n", StringComparison.Ordinal);

        Assert.Equal(expected, await CodeFixTestHarness.FixStatementGroups(source, fixAll: true));
    }

    /// <summary>
    /// Verifies each change of group gets one blank line and each group loses the blank lines inside it: kinds
    /// that differ, event subscriptions apart from arithmetic, argument checks and awaits apart from other calls,
    /// declarations and assignments of awaited results apart from other declarations, assignments, and awaits,
    /// increments with assignments, comment lines that open a group, trailing comments that do not, statements
    /// that stand alone, statements sharing a line, and statements written over several lines, which stand alone
    /// even beside another of their kind.
    /// </summary>
    /// <param name="written">The statements as written, one per line.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("int first = 1;\nint second = 2;\n_count = first + second;\nConsole.WriteLine(name);\nawait pending;\nChanged += (sender, args) => action();",
        "int first = 1;\nint second = 2;\n\n_count = first + second;\n\nConsole.WriteLine(name);\n\nawait pending;\n\nChanged += (sender, args) => action();")]
    [InlineData("int first = 1;\n\n\nint second = 2;\nConsole.WriteLine(first);\n\nConsole.WriteLine(second);",
        "int first = 1;\nint second = 2;\n\nConsole.WriteLine(first);\nConsole.WriteLine(second);")]
    [InlineData("ArgumentNullException.ThrowIfNull(name);\nArgumentException.ThrowIfNullOrEmpty(name);\nConsole.WriteLine(name);",
        "ArgumentNullException.ThrowIfNull(name);\nArgumentException.ThrowIfNullOrEmpty(name);\n\nConsole.WriteLine(name);")]
    [InlineData("_count += 1;\nChanged += (sender, args) => action();\nChanged -= (sender, args) => action();\n_count -= 1;",
        "_count += 1;\n\nChanged += (sender, args) => action();\nChanged -= (sender, args) => action();\n\n_count -= 1;")]
    [InlineData("await pending;\n\nawait pending;\naction();", "await pending;\nawait pending;\n\naction();")]
    [InlineData("int first = 1;\nstring text = await reading;\nstring copy = await reading;\nint second = 2;",
        "int first = 1;\n\nstring text = await reading;\nstring copy = await reading;\n\nint second = 2;")]
    [InlineData("_count = 1;\nname = await reading;\nname += await reading.ConfigureAwait(continueOnCapturedContext: false);\nawait pending;\nstring text = await reading;",
        "_count = 1;\n\nname = await reading;\nname += await reading.ConfigureAwait(continueOnCapturedContext: false);\n\nawait pending;\n\nstring text = await reading;")]
    [InlineData("_count++;\n\n_count = 2;\n--_count;", "_count++;\n_count = 2;\n--_count;")]
    [InlineData("int first = 1;\n// The second value doubles the first.\n\nint second = first * 2;",
        "int first = 1;\n\n// The second value doubles the first.\nint second = first * 2;")]
    [InlineData("int first = 1;\n// Kept for the log.\n\n// Doubled for the total.\nint second = first * 2;",
        "int first = 1;\n\n// Kept for the log.\n// Doubled for the total.\nint second = first * 2;")]
    [InlineData("int first = 1; // One.\n\nint second = 2;", "int first = 1; // One.\nint second = 2;")]
    [InlineData("int first = 1;\n{\n    Console.WriteLine(first);\n}\nint second = 2;",
        "int first = 1;\n\n{\n    Console.WriteLine(first);\n}\n\nint second = 2;")]
    [InlineData("int first = 1;\nvoid Log() => Console.WriteLine(first);\nLog();",
        "int first = 1;\n\nvoid Log() => Console.WriteLine(first);\n\nLog();")]
    [InlineData("int first = 1; Console.WriteLine(first);", "int first = 1;\n\nConsole.WriteLine(first);")]
    [InlineData("int first = 1;\nConsole.WriteLine(name,\n    first);\nConsole.WriteLine(name);",
        "int first = 1;\n\nConsole.WriteLine(name,\n    first);\n\nConsole.WriteLine(name);")]
    [InlineData("_count = _count\n    + 1;\n_count = _count\n    + 2;\n_count = 0;",
        "_count = _count\n    + 1;\n\n_count = _count\n    + 2;\n\n_count = 0;")]
    public async Task RegroupsStatements(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixStatementGroups(Wrap(written), fixAll: true));
    }

    /// <summary>
    /// Verifies top-level statements are grouped like any other statements.
    /// </summary>
    [Fact]
    public async Task RegroupsTopLevelStatements()
    {
        const string source = "using System;\n\nint first = 1;\n\nint second = 2;\nConsole.WriteLine(first + second);\n";
        const string expected = "using System;\n\nint first = 1;\nint second = 2;\n\nConsole.WriteLine(first + second);\n";

        Assert.Equal(expected, await CodeFixTestHarness.FixStatementGroups(source, fixAll: true));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
