using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.SourceFiles;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.SourceFiles;

/// <summary>
/// Covers the source file length limit and the comment lines it leaves out of the count.
/// </summary>
public sealed class SourceFileLengthAnalyzerTests
{
    private const string Declaration = "public sealed class ExampleType { }";

    /// <summary>
    /// Verifies lines holding code still count when a comment shares them, before or after the code or as the
    /// end of a block comment, and that blank lines outside comments count too. Each body brings the method to
    /// exactly one thousand counted lines.
    /// </summary>
    /// <param name="body">Lines written 998 times inside a method, adding one counted line each time.</param>
    [Theory]
    [InlineData("; // note")]
    [InlineData("/* note */ ;")]
    [InlineData("/* note\n */ ;")]
    [InlineData("")]
    public async Task CountsLinesHoldingCode(string body)
    {
        string source = $"public sealed class ExampleType {{ public void Execute() {{\n{Repeat(body, count: 998)}}} }}\n";
        Diagnostic diagnostic = Assert.Single(await AnalyzerTestHarness.Analyze(new SourceFileLengthAnalyzer(), source));

        Assert.Equal(SourceFileLengthAnalyzer.RuleIdentifier, diagnostic.Id);

        Assert.StartsWith(expectedStartString: "This file has 1000 lines besides comments, and authored files may have at most 999. Split it by responsibility",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the authored source file length limit, counting the required final newline as a terminator.
    /// </summary>
    /// <param name="authoredLineCountText">The number of authored lines, each closed by a newline.</param>
    [Theory]
    [InlineData("999")]
    [InlineData("1000")]
    public async Task EnforcesSourceFileLengthBoundary(string authoredLineCountText)
    {
        int authoredLineCount = int.Parse(authoredLineCountText, CultureInfo.InvariantCulture);
        string source = Declaration + new string(c: '\n', authoredLineCount);

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new SourceFileLengthAnalyzer(), source);

        Assert.Equal(authoredLineCount > 999 ? 1 : 0, diagnostics.Length);
    }

    /// <summary>
    /// Verifies lines holding only comments are left out of the count, for every kind of comment: line comments,
    /// indented ones, XML documentation comments in both forms, block comments, and several comments on one line.
    /// The file keeps 999 other lines, so counting any of the thousand comment lines would report it.
    /// </summary>
    /// <param name="comment">A line holding only comments, written a thousand times above the declaration.</param>
    [Theory]
    [InlineData("// note")]
    [InlineData("    // indented note")]
    [InlineData("/// <para>Note.</para>")]
    [InlineData("/** <para>Note.</para> */")]
    [InlineData("/* note */")]
    [InlineData("/* first */ /* second */ // third")]
    public async Task IgnoresCommentLines(string comment)
    {
        string source = Repeat(comment, count: 1_000) + Declaration + new string(c: '\n', count: 999);

        Assert.Empty(await AnalyzerTestHarness.Analyze(new SourceFileLengthAnalyzer(), source));
    }

    /// <summary>
    /// Verifies every line of a block comment is left out, from the line that opens it to the line that closes
    /// it, including blank lines inside it and the lines of a <c>/** */</c> documentation comment.
    /// </summary>
    /// <param name="opening">The line that opens the comment.</param>
    /// <param name="body">A line inside the comment, written a thousand times.</param>
    /// <param name="closing">The line that closes the comment.</param>
    [Theory]
    [InlineData("/*", "", "*/")]
    [InlineData("/*", " * note", " */")]
    [InlineData("/**", " * <para>Note.</para>", " */")]
    public async Task IgnoresEveryLineOfBlockComments(string opening, string body, string closing)
    {
        string comment = $"{opening}\n{Repeat(body, count: 1_000)}{closing}\n";
        string source = comment + Declaration + new string(c: '\n', count: 999);

        Assert.Empty(await AnalyzerTestHarness.Analyze(new SourceFileLengthAnalyzer(), source));
    }

    private static string Repeat(string line, int count)
    {
        return string.Concat(Enumerable.Repeat(line + "\n", count));
    }
}
