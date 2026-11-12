using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>
/// Verifies brace fixes in both directions, including conditional consistency and trivia preservation.
/// </summary>
public sealed class ControlFlowBracesCodeFixTests
{
    /// <summary>
    /// Verifies a multiline branch requires braces on all branches, including single-line else bodies.
    /// </summary>
    [Fact]
    public async Task AddsBracesThroughoutMixedChains()
    {
        const string source = """
            public static class ExampleType
            {
                public static void Execute(int count)
                {
                    if (count > 0)
                        count = System.Math.Abs(count)
                            .GetHashCode();
                    else if (count < 0)
                        return;
                    else
                        return;
                }
            }
            """;

        const string expected = """
            public static class ExampleType
            {
                public static void Execute(int count)
                {
                    if (count > 0)
                    {
                        count = System.Math.Abs(count)
                            .GetHashCode();
                    }
                    else if (count < 0)
                    {
                        return;
                    }
                    else
                    {
                        return;
                    }
                }
            }
            """;

        Assert.Equal(expected, await CodeFixTestHarness.FixBraces(source, fixAll: true));
    }

    /// <summary>
    /// Verifies an unbraced multiline body gains braces without padding inside the block.
    /// </summary>
    /// <param name="header">The statement controlling the body.</param>
    /// <param name="suffix">The trailing condition for a do loop, if present.</param>
    [Theory]
    [InlineData("if (count > 0)", "")]
    [InlineData("while (count > 0)", "")]
    [InlineData("do", "        while (count > 0);\n")]
    [InlineData("for (int index = 0; index < count; index++)", "")]
    [InlineData("foreach (int value in values)", "")]
    [InlineData("using (var stream = new System.IO.MemoryStream())", "")]
    [InlineData("lock (values)", "")]
    [InlineData("fixed (int* pointer = values)", "")]
    public async Task AddsMultilineBodyBraces(string header, string suffix)
    {
        string source = $$"""
            public static class ExampleType
            {
                public static unsafe void Execute(int[] values, int count)
                {
                    {{header}}
                        count = System.Math.Abs(
                            count - 1);
            {{suffix}}    }
            }
            """;

        string expected = source.Replace(oldValue: "            count =", newValue: "        {\n            count =", StringComparison.Ordinal)
            .Replace(oldValue: "                count - 1);\n", newValue: "                count - 1);\n        }\n", StringComparison.Ordinal);

        Assert.Equal(expected, await CodeFixTestHarness.FixBraces(source, fixAll: true));
    }

    /// <summary>
    /// Verifies a statement block that keeps its braces puts each brace and statement on a line of its
    /// own, empty blocks included, in the line ending the document uses (#49).
    /// </summary>
    /// <param name="statements">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData(
        "try { count++; }\n        catch (System.InvalidOperationException exception) { System.Console.WriteLine(exception); }",
        "try\n        {\n            count++;\n        }\n        catch (System.InvalidOperationException exception)\n        {\n            System.Console.WriteLine(exception);\n        }"
    )]
    [InlineData("try { count++; } finally { }", "try\n        {\n            count++;\n        }\n        finally\n        {\n        }")]
    [InlineData(
        "try\n        { count++; }\n        finally\n        { count--; }",
        "try\n        {\n            count++;\n        }\n        finally\n        {\n            count--;\n        }"
    )]
    [InlineData("while (count > 0) { }", "while (count > 0)\n        {\n        }")]
    [InlineData("if (count > 0) { count++; count--; }", "if (count > 0)\n        {\n            count++;\n            count--;\n        }")]
    [InlineData(
        "if (count > 0) { count++; count--; } else return;",
        "if (count > 0)\n        {\n            count++;\n            count--;\n        }\n        else\n        {\n            return;\n        }"
    )]
    [InlineData("checked { count++; }", "checked\n        {\n            count++;\n        }")]
    [InlineData("{ int result = count; }", "{\n            int result = count;\n        }")]
    [InlineData(
        "switch (count)\n        {\n            case 0: { count++; break; }\n        }",
        "switch (count)\n        {\n            case 0:\n                {\n                    count++;\n                    break;\n                }\n        }"
    )]
    public async Task ExpandsOneLineStatementBlocks(string statements, string expected)
    {
        const string member = "public static class ExampleType\n{\n    public static void Execute(int count)\n    {\n        STATEMENTS\n    }\n}";

        foreach (string lineEnding in new[] { "\n", "\r\n" })
        {
            string source = member.Replace(oldValue: "STATEMENTS", statements, StringComparison.Ordinal).Replace(oldValue: "\n", lineEnding, StringComparison.Ordinal);
            string target = member.Replace(oldValue: "STATEMENTS", expected, StringComparison.Ordinal).Replace(oldValue: "\n", lineEnding, StringComparison.Ordinal);
            string result = await CodeFixTestHarness.FixBraces(source, fixAll: true);

            Assert.Equal(target, result);
            Assert.Empty(await AnalyzerTestHarness.Analyze(new ControlFlowBracesAnalyzer(), result));
        }
    }

    /// <summary>
    /// Verifies simple case bodies lose braces and multiline or multiple-statement case bodies gain them.
    /// </summary>
    [Fact]
    public async Task FixesCaseBodiesInBothDirections()
    {
        const string source = """
            public static class ExampleType
            {
                public static int Execute(int count)
                {
                    switch (count)
                    {
                        case 0:
                        {
                            return count;
                        }
                        case 1:
                            count++;

                            return count;
                        default:
                            return System.Math.Abs(
                                count);
                    }
                }
            }
            """;

        const string expected = """
            public static class ExampleType
            {
                public static int Execute(int count)
                {
                    switch (count)
                    {
                        case 0:
                            return count;
                        case 1:
                            {
                                count++;

                                return count;
                            }
                        default:
                            {
                                return System.Math.Abs(
                                    count);
                            }
                    }
                }
            }
            """;

        Assert.Equal(expected, await CodeFixTestHarness.FixBraces(source, fixAll: true));
    }

    /// <summary>
    /// Verifies a multiline header does not make its single-line body require braces.
    /// </summary>
    [Fact]
    public async Task MeasuresTheBodyRatherThanTheHeader()
    {
        const string source = """
            public static class ExampleType
            {
                public static void Execute(int[] values)
                {
                    foreach (int value in
                        values)
                    {
                        System.Console.WriteLine(value);
                    }
                }
            }
            """;

        string expected = source.Replace(oldValue: "        {\n", string.Empty, StringComparison.Ordinal)
            .Replace(oldValue: "        }\n", string.Empty, StringComparison.Ordinal);

        Assert.Equal(expected, await CodeFixTestHarness.FixBraces(source, fixAll: true));
    }

    /// <summary>
    /// Verifies comments around removed and inserted braces retain their content and line endings.
    /// </summary>
    /// <param name="lineEnding">The document's existing line ending.</param>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task PreservesCommentsAndLineEndings(string lineEnding)
    {
        string source = string.Join(
            lineEnding,
            value:
        [
            "public static class ExampleType", "{", "    public static void Execute(int count)", "    {",
            "        while (count > 0) // loop", "        { // opening", "            // body",
            "            count--; // decrement", "        } // closing", "",
            "        if (count < 0) // condition", "            // explanation",
            "            count = System.Math.Abs(", "                count); // result", "    }", "}",
        ]
        );

        string result = await CodeFixTestHarness.FixBraces(source, fixAll: true);

        foreach (string comment in new[] { "loop", "opening", "body", "decrement", "closing", "condition", "explanation", "result" })
            Assert.Contains("// " + comment + lineEnding, result, StringComparison.Ordinal);

        Assert.DoesNotContain(expectedSubstring: "\n", result.Replace(lineEnding, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Empty(await AnalyzerTestHarness.Analyze(new StatementSpacingAnalyzer(), result));
    }

    /// <summary>
    /// Verifies nested fixes retain the original else binding and reach a stable result.
    /// </summary>
    [Fact]
    public async Task PreservesDanglingElseBindingDuringFixAll()
    {
        const string source = """
            public static class ExampleType
            {
                public static void Execute(int count)
                {
                    if (count > 0)
                    {
                        while (count > 1)
                        {
                            if (count > 2) return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }
            }
            """;

        string result = await CodeFixTestHarness.FixBraces(source, fixAll: true);
        SyntaxNode root = await CSharpSyntaxTree.ParseText(result).GetRootAsync();
        IfStatementSyntax[] conditionals = [.. root.DescendantNodes().OfType<IfStatementSyntax>()];
        Assert.NotNull(conditionals[0].Else);
        Assert.Null(conditionals[1].Else);
        Assert.True(conditionals[0].Statement is BlockSyntax { Statements.Count: 1 });
        Assert.Empty(await AnalyzerTestHarness.Analyze(new ControlFlowBracesAnalyzer(), result));
    }

    /// <summary>
    /// Verifies expanding a loop block an else depends on moves the loop into braces of its own instead
    /// of letting the else bind to the inner conditional.
    /// </summary>
    [Fact]
    public async Task PreservesDanglingElseBindingWhileExpandingBlocks()
    {
        const string source = """
            public static class ExampleType
            {
                public static void Execute(int count)
                {
                    if (count > 0) while (count > 1) { if (count > 2) return; } else return;
                }
            }
            """;

        string result = await CodeFixTestHarness.FixBraces(source, fixAll: true);
        SyntaxNode root = await CSharpSyntaxTree.ParseText(result).GetRootAsync();
        IfStatementSyntax[] conditionals = [.. root.DescendantNodes().OfType<IfStatementSyntax>()];
        Assert.NotNull(conditionals[0].Else);
        Assert.Null(conditionals[1].Else);
        Assert.True(conditionals[0].Statement is BlockSyntax { Statements: [WhileStatementSyntax] });
        Assert.Empty(await AnalyzerTestHarness.Analyze(new ControlFlowBracesAnalyzer(), result));
    }

    /// <summary>
    /// Verifies every branch in a simple conditional chain can lose its braces together.
    /// </summary>
    [Fact]
    public async Task RemovesBracesThroughoutSimpleChains()
    {
        const string source = """
            public static class ExampleType
            {
                public static void Execute(int count)
                {
                    if (count > 0)
                    {
                        return;
                    }
                    else if (count < 0)
                    {
                        return;
                    }
                    else
                    {
                        return;
                    }
                }
            }
            """;

        string expected = source.Replace(oldValue: "        {\n", string.Empty, StringComparison.Ordinal)
            .Replace(oldValue: "        }\n", string.Empty, StringComparison.Ordinal);

        Assert.Equal(expected, await CodeFixTestHarness.FixBraces(source, fixAll: true));
    }

    /// <summary>
    /// Verifies a single-line body loses its braces for every embedded-statement construct.
    /// </summary>
    /// <param name="header">The statement controlling the body.</param>
    /// <param name="suffix">The trailing condition for a do loop, if present.</param>
    [Theory]
    [InlineData("if (count > 0)", "")]
    [InlineData("while (count > 0)", "")]
    [InlineData("do", " while (count > 0);")]
    [InlineData("for (int index = 0; index < count; index++)", "")]
    [InlineData("foreach (int value in values)", "")]
    [InlineData("using (var stream = new System.IO.MemoryStream())", "")]
    [InlineData("lock (values)", "")]
    [InlineData("fixed (int* pointer = values)", "")]
    public async Task RemovesSingleLineBodyBraces(string header, string suffix)
    {
        ArgumentNullException.ThrowIfNull(suffix);

        string source = $$"""
            public static class ExampleType
            {
                public static unsafe void Execute(int[] values, int count)
                {
                    {{header}}
                    {
                        count--;
                    }{{suffix}}
                }
            }
            """;

        string expected = source.Replace(oldValue: "        {\n", string.Empty, StringComparison.Ordinal)
            .Replace(
                "        }" + suffix + "\n",
                suffix.Length == 0 ? string.Empty : "        " + suffix.TrimStart() + "\n",
                StringComparison.Ordinal
            );

        Assert.Equal(expected, await CodeFixTestHarness.FixBraces(source, fixAll: false));
    }
}
