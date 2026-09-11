using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>
/// Covers spacing at statement boundaries without inserting padding inside blocks.
/// </summary>
public sealed class StatementSpacingAnalyzerTests
{
    /// <summary>
    /// Verifies separation before control flow and multiline declarations.
    /// </summary>
    /// <param name="memberSource">The method containing exactly one missing separator.</param>
    [Theory]
    [InlineData("public static void Execute(int count) { count++;\nif (count > 0) { } }")]
    [InlineData("public static void Execute(int count) { count++;\nfor (int index = 0; index < count; index++) { } }")]
    [InlineData("public static void Execute(int[] values) { System.Console.WriteLine();\nforeach (int value in values) { } }")]
    [InlineData("public static void Execute((int, int)[] values) { System.Console.WriteLine();\nforeach (var (first, second) in values) { } }")]
    [InlineData("public static void Execute(int count) { count++;\nwhile (count > 0) { count--; } }")]
    [InlineData("public static void Execute(int count) { count++;\ndo { count--; } while (count > 0); }")]
    [InlineData("public static void Execute(int count) { count++;\nswitch (count) { default: break; } }")]
    [InlineData("public static void Execute(int count) { count++;\ntry { } finally { } }")]
    [InlineData("public static void Execute(int count) { count++;\nusing (new System.IO.MemoryStream()) { } }")]
    [InlineData("public static void Execute(int count) { count++;\nusing var stream = new System.IO.MemoryStream(); }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute(int count) { count++;\nawait using var stream = new System.IO.MemoryStream(); }")]
    [InlineData("public static void Execute(int count) { count++;\nreturn; }")]
    [InlineData("public static void Execute(int count) { count++;\nthrow new System.InvalidOperationException(); }")]
    [InlineData("public static void Execute(int count) { while (count > 0) { count++;\nbreak; } }")]
    [InlineData("public static void Execute(int count) { while (count > 0) { count--;\ncontinue; } }")]
    [InlineData("public static System.Collections.Generic.IEnumerable<int> Execute(int count) { count++;\nyield return count; }")]
    [InlineData("public static System.Collections.Generic.IEnumerable<int> Execute(int count) { count++;\nyield break; }")]
    [InlineData("public static void Execute(int count) { count++;\nint result =\ncount + 1; }")]
    [InlineData("public static void Execute(int count) { int result =\ncount + 1;\ncount++; }")]
    [InlineData("public static void Execute(int count) { switch (count) { case 0: count++;\nbreak; } }")]
    public async Task RequiresBlankLinesBetweenStatements(string memberSource)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new StatementSpacingAnalyzer(), $"public static class ExampleType {{ {memberSource} }}");

        Assert.Equal(StatementSpacingAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Verifies separation after an unbraced conditional while leaving its body attached.
    /// </summary>
    [Fact]
    public async Task RequiresBlankLineAfterUnbracedConditional()
    {
        const string source = "public static class ExampleType { public static void Execute(int[] numbers) { for (int index = 0; index < numbers.Length; index++) { if (index > 10)\nbreak;\nnumbers[index] += index; } } }";
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new StatementSpacingAnalyzer(), source);

        Assert.Equal(StatementSpacingAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
        Assert.Equal(expected: "numbers", actual: source.Substring(Assert.Single(diagnostics).Location.SourceSpan.Start,
            Assert.Single(diagnostics).Location.SourceSpan.Length));
    }

    /// <summary>
    /// Verifies that block edges, embedded bodies, and existing blank lines remain unchanged.
    /// </summary>
    /// <param name="memberSource">The method with valid statement separation.</param>
    [Theory]
    [InlineData("public static int Execute() {\nreturn 0;\n}")]
    [InlineData("public static void Execute(int count) {\nint result =\ncount + 1;\n}")]
    [InlineData("public static void Execute(int count) {\nif (count > 0)\nreturn;\n}")]
    [InlineData("public static void Execute(int count) { if (count > 0) { return; } else if (count < 0) { throw new System.InvalidOperationException(); } else { return; } }")]
    [InlineData("public static void Execute() { try { return; } catch (System.InvalidOperationException) { throw; } finally { System.Console.WriteLine(); } }")]
    [InlineData("public static int Execute() { int count = 0;\n\nreturn count; }")]
    [InlineData("public static int Execute() { int count = 0;\n \t\nreturn count; }")]
    [InlineData("public static int Execute() { int count = 0; // prior statement\n\n// result\nreturn count; }")]
    [InlineData("public static void Execute(int count) { int first = count;\nint second = first + 1; }")]
    [InlineData("public static void Execute(int count) {\nusing var stream = new System.IO.MemoryStream();\n}")]
    public async Task PreservesBlockEdgesAndExistingSeparators(string memberSource)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new StatementSpacingAnalyzer(), $"public static class ExampleType {{ {memberSource} }}");

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies that a multiline call chain beginning a method does not acquire leading padding.
    /// </summary>
    [Fact]
    public async Task PreservesMethodStartBeforeMultilineCallChain()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;

            public static class ExampleType
            {
                public static async Task<int> ProcessAsync(IEnumerable<int> values, CancellationToken cancellationToken)
                {
                    var numbers = values
                        .Where(number => number > 0)
                        .Select(number => number * 2)
                        .Distinct()
                        .OrderBy(number => number)
                        .ToList();

                    return numbers.Count;
                }
            }
            """;

        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new StatementSpacingAnalyzer(), source);

        Assert.Empty(diagnostics);
    }
}
