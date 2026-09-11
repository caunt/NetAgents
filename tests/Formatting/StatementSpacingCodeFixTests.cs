using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>
/// Verifies precise whitespace edits through individual and batch code fixes.
/// </summary>
public sealed class StatementSpacingCodeFixTests
{
    /// <summary>
    /// Verifies blank lines before control flow and after an unbraced conditional.
    /// </summary>
    [Fact]
    public async Task SeparatesControlFlowWithoutPaddingBlocks()
    {
        const string source = """
            public static class ExampleType
            {
                public static int Execute(int[] numbers)
                {
                    int result = 0;
                    foreach (int number in numbers)
                    {
                        if (number > 10)
                            break;
                        result += number;
                    }
                    return result;
                }
            }
            """;

        const string expected = """
            public static class ExampleType
            {
                public static int Execute(int[] numbers)
                {
                    int result = 0;

                    foreach (int number in numbers)
                    {
                        if (number > 10)
                            break;

                        result += number;
                    }

                    return result;
                }
            }
            """;

        Assert.Equal(expected, await CodeFixTestHarness.FixSpacing(source, fixAll: true));
    }

    /// <summary>
    /// Verifies multiline declarations are separated while the first declaration stays beside the opening brace.
    /// </summary>
    [Fact]
    public async Task SeparatesMultilineDeclarations()
    {
        const string source = """
            public static class ExampleType
            {
                public static int Execute(int[] numbers)
                {
                    var result = numbers.Length switch
                    {
                        0 => 0,
                        _ => numbers[0]
                    };
                    var description =
                        result > 100
                            ? "Large result"
                            : "Small result";
                    var options = new
                    {
                        Result = result,
                        Description = description
                    };
                    return options.Result;
                }
            }
            """;

        string expected = source.Replace(oldValue: "        var description", newValue: "\n        var description", StringComparison.Ordinal)
            .Replace(oldValue: "        var options", newValue: "\n        var options", StringComparison.Ordinal)
            .Replace(oldValue: "        return options.Result;", newValue: "\n        return options.Result;", StringComparison.Ordinal);

        Assert.Equal(expected, await CodeFixTestHarness.FixSpacing(source, fixAll: true));
    }

    /// <summary>
    /// Verifies comments and both common newline styles survive an individual fix.
    /// </summary>
    /// <param name="lineEnding">The document's existing line ending.</param>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task PreservesCommentsAndLineEndings(string lineEnding)
    {
        string source = string.Join(lineEnding, value:
            ["public static class ExampleType", "{", "    public static int Execute()", "    {",
            "        int count = 0; // retain this comment", "        // explain the result", "        return count;", "    }", "}"]);

        string expected = source.Replace(oldValue: "        // explain the result", newValue: lineEnding + "        // explain the result", StringComparison.Ordinal);

        Assert.Equal(expected, await CodeFixTestHarness.FixSpacing(source, fixAll: false));
    }

    /// <summary>
    /// Verifies empty lines inside block comments do not count as statement separators.
    /// </summary>
    [Fact]
    public async Task KeepsMultilineCommentsAttached()
    {
        const string source = "public static class ExampleType\n{\n    public static int Execute()\n    {\n        int count = 0;\n        /* result\n\n           explanation */\n        return count;\n    }\n}";
        string expected = source.Replace(oldValue: "        /* result", newValue: "\n        /* result", StringComparison.Ordinal);

        Assert.Equal(expected, await CodeFixTestHarness.FixSpacing(source, fixAll: false));
    }

    /// <summary>
    /// Verifies statements sharing a source line receive an empty line without trailing spaces.
    /// </summary>
    [Fact]
    public async Task SeparatesStatementsOnTheSameLine()
    {
        const string source = "public static class ExampleType\n{\n    public static int Execute()\n    {\n        int count = 0; return count;\n    }\n}";
        string expected = source.Replace(oldValue: "; return count;", newValue: ";\n\n        return count;", StringComparison.Ordinal);

        Assert.Equal(expected, await CodeFixTestHarness.FixSpacing(source, fixAll: false));
    }

    /// <summary>
    /// Verifies top-level control flow is separated without inserting a leading empty line.
    /// </summary>
    [Fact]
    public async Task SeparatesTopLevelStatements()
    {
        const string source = "int count = 0;\nif (count > 10)\n    return;\ncount++;\n";
        const string expected = "int count = 0;\n\nif (count > 10)\n    return;\n\ncount++;\n";

        Assert.Equal(expected, await CodeFixTestHarness.FixSpacing(source, fixAll: true));
    }
}
