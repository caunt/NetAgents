using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>
/// Verifies argument and parameter layout for declarations and calls.
/// </summary>
public sealed class ArgumentLayoutAnalyzerTests
{
    /// <summary>
    /// Accepts compact lists and expanded lists whose content cannot safely occupy one line.
    /// </summary>
    /// <param name="source">The correctly laid out source.</param>
    [Theory]
    [InlineData("public record ExampleType(string Value, int Count);")]
    [InlineData("public class ExampleType { public void Execute(string value, int count) { Execute(value, count); } }")]
    [InlineData("public class ExampleType { public void Execute(\n    // Explanation\n    string value) { } }")]
    [InlineData("public class ExampleType { public void Execute(\n    /* Explanation\n    continued */ string value) { } }")]
    [InlineData("public class ExampleType { public void Execute(string value) // Implements the contract\n    { } }")]
    [InlineData("public class ExampleType { public string Execute(string value) => value.Substring(startIndex: 1) // Skips the prefix\n        .Trim(); }")]
    [InlineData("public class ExampleType { public System.Threading.Tasks.Task Execute() => System.Threading.Tasks.Task.Run(() => { System.Console.WriteLine(); }); }")]
    [InlineData("public class ExampleType\n{\n    public System.Threading.Tasks.Task<bool> Execute() => System.Threading.Tasks.Task.Run(() =>\n    {\n        int length = System.Environment.ProcessorCount;\n\n        return length > 0;\n    });\n}")]
    [InlineData("public record ExampleType(string aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa,\n    int bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb);")]
    [InlineData("public class ExampleType { public void Execute(int first, int second) { Execute(1, // explanation\n    2); } }")]
    [InlineData("public class ExampleType { public void Execute() { System.Console.WriteLine(\"An intentionally long call argument that exceeds one hundred and twenty-eight characters, which a single item may do on one line.\"); } }")]
    [InlineData("public class ExampleType { public void Execute(string value) { string.Concat(string.Concat(\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\n        \"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\n        \"x\"),\n    value,\n    value); } }")]
    [InlineData("public class ExampleType { public void Execute() { System.Console.WriteLine(string.Concat(\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\n    \"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\n    \"x\")); } }")]
    public async Task AcceptsValidLayouts(string source)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source));
    }

    /// <summary>
    /// Decides a one-line list exactly as its normalized text does, across the whole length boundary.
    /// </summary>
    /// <remarks>
    /// The analyzer brackets the normalized length by the token widths instead of building that
    /// text, so the risk is a bound that is wrong rather than one that is slow. A list written on
    /// one line is reported precisely when it cannot stay there, which makes the diagnostic a
    /// direct read of that decision and a bound that is too tight fail here.
    /// </remarks>
    /// <param name="trailer">Content placed before the second parameter, inside the list.</param>
    /// <exception cref="ArgumentNullException">Thrown when a data row supplies no <paramref name="trailer"/>; an empty string stands for no extra content.</exception>
    [Theory]
    [InlineData("")]
    [InlineData("/* Explanation */ ")]
    [InlineData("/* One */ /* Two */ ")]
    public async Task DecidesOneLineListsAsTheirNormalizedTextDoes(string trailer)
    {
        ArgumentNullException.ThrowIfNull(trailer);

        for (int width = 100; width <= 150; width++)
        {
            string source = $"public class ExampleType {{ public void Execute(string {new string(c: 'a', width)}, {trailer}int count) {{ }} }}";

            Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source);

            Assert.Equal(ExceedsOneLine(source), diagnostics.Length > 0);
        }
    }

    /// <summary>
    /// Wraps a call's three arguments when, written on one line, they end past column 128 where the list stands,
    /// although their own contents fit, and keeps them together inside a chain that splits, which breaks the line
    /// instead.
    /// </summary>
    /// <param name="endText">The column the argument list ends at when written on one line.</param>
    [Theory]
    [InlineData("127")]
    [InlineData("128")]
    [InlineData("129")]
    public async Task EnforcesCallBoundary(string endText)
    {
        int end = int.Parse(endText, CultureInfo.InvariantCulture);

        // The list opens at column 21, after the indentation and the call's name, and closes two characters after
        // its twelve-character frame of quotes and separators.
        string arguments = "\"" + new string(c: 'x', end - 35) + "\", \"y\", \"z\"";
        string source = $"public class ExampleType\n{{\n    public void Execute()\n    {{\n        string.Concat({arguments});\n    }}\n}}\n";

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source);

        Assert.Equal(end > 128 ? 1 : 0, diagnostics.Length);

        string chained = $"public class ExampleType\n{{\n    public int Execute()\n    {{\n        return string.Concat({arguments})\n            .Trim()\n            .Length;\n    }}\n}}\n";

        Assert.Empty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), chained));
    }

    /// <summary>
    /// Uses compact parameter text, excluding delimiters and the declaration prefix.
    /// </summary>
    /// <param name="lengthText">The exact compact parameter length.</param>
    [Theory]
    [InlineData("127")]
    [InlineData("128")]
    [InlineData("129")]
    public async Task EnforcesExactBoundary(string lengthText)
    {
        int length = int.Parse(lengthText, CultureInfo.InvariantCulture);
        string parameter = "string " + new string(c: 'p', length - 18);
        string source = $"public class ExampleType {{ public void VeryLongMethodNameThatDoesNotCountTowardsTheLimit({parameter}, int count) {{ }} }}";

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source);

        Assert.Equal(length > 128 ? 1 : 0, diagnostics.Length);

        string expanded = $"public class ExampleType {{ public void Execute({parameter},\n    int count) {{ }} }}";

        Diagnostic[] expandedDiagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), expanded);

        Assert.Equal(length > 128 ? 0 : 1, expandedDiagnostics.Length);
    }

    /// <summary>
    /// Expands a list whose own literal carries a line break, which no length bound can decide.
    /// </summary>
    [Fact]
    public async Task ExpandsListsHoldingAMultilineLiteral()
    {
        const string literal = "@\"verbatim\nsecond line\"";
        string compact = $"public class ExampleType {{ public void Execute(string value = {literal}, int count = 0) {{ }} }}";
        string expanded = $"public class ExampleType {{ public void Execute(string value = {literal},\n    int count = 0) {{ }} }}";

        Assert.Equal(ArgumentLayoutAnalyzer.RuleIdentifier,
            Assert.Single(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), compact)).Id);

        Assert.Empty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), expanded));
    }

    /// <summary>
    /// Keeps a call's single argument, or two whose second is a name, a member access, a <c>nameof</c>, or a literal
    /// other than a string, together however long the line or the arguments are while nothing inside them has to
    /// break, and reports them once a line break separates them.
    /// </summary>
    /// <param name="arguments">The arguments of the call, written on one line.</param>
    /// <exception cref="ArgumentNullException">Thrown when a data row supplies no <paramref name="arguments"/> to place in the call.</exception>
    [Theory]
    [InlineData("\"an argument long enough that this call ends well past the one hundred and twenty-eight character limit of its line\"")]
    [InlineData("\"a first argument long enough to push the call well past the one hundred and twenty-eight character limit of its line\", string.Empty")]
    [InlineData("\"a first argument long enough to push the call well past the one hundred and twenty-eight character limit of its line\", nameof(Execute)")]
    [InlineData("\"a first argument that alone is long enough to pass the limit of one hundred and twenty-eight characters on the contents of a list\", 0")]
    public async Task KeepsShortCallsTogether(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string together = $"public class ExampleType\n{{\n    public void Execute()\n    {{\n        string.Concat({arguments});\n    }}\n}}\n";

        Assert.Empty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), together));

        string separated = together.Replace(oldValue: "string.Concat(\"",
            newValue: "string.Concat(\n            \"",
            StringComparison.Ordinal);

        Assert.Equal(ArgumentLayoutAnalyzer.RuleIdentifier,
            Assert.Single(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), separated)).Id);
    }

    /// <summary>
    /// Rejects short expanded signatures across declaration forms.
    /// </summary>
    /// <param name="source">A declaration containing one incorrectly expanded parameter list.</param>
    [Theory]
    [InlineData("public class ExampleType { public void Execute(\nstring value,\nint count\n) { } }")]
    [InlineData("public class ExampleType { public ExampleType(\nstring value\n) { } }")]
    [InlineData("public class ExampleType(\nstring value\n) { }")]
    [InlineData("public record ExampleType(\nstring Value,\nint Count\n);")]
    [InlineData("public readonly record struct ExampleType(\nstring Value\n);")]
    [InlineData("public delegate void ExampleType(\nstring value\n);")]
    [InlineData("public class ExampleType { public string this[\nint index\n] => string.Empty; }")]
    [InlineData("public class ExampleType { public void Execute() { void Local(\nint value\n) { } Local(0); } }")]
    [InlineData("public class ExampleType { public static ExampleType operator +(\nExampleType first,\nExampleType second\n) => first; }")]
    [InlineData("public class ExampleType { public System.Func<int, int> Execute() => (\nint value\n) => value; }")]
    [InlineData("public class ExampleType { public System.Func<int, int> Execute() => delegate(\nint value\n) { return value; }; }")]
    [InlineData("public class ExampleType { public void Execute(\n) { } }")]
    [InlineData("public class ExampleType { public void Execute(\n[System.Runtime.InteropServices.In] string value\n) { } }")]
    public async Task RejectsExpandedShortDeclarations(string source)
    {
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source);

        Assert.Equal(ArgumentLayoutAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Rejects incorrectly wrapped calls as well as declarations.
    /// </summary>
    /// <param name="source">A source with one incorrectly laid out argument list.</param>
    [Theory]
    [InlineData("public class ExampleType { public void Execute(string value, int count) { Execute(\nvalue,\ncount\n); } }")]
    [InlineData("public class ExampleType { public ExampleType(string value) { } public static ExampleType Create() => new ExampleType(\nstring.Empty\n); }")]
    [InlineData("public class ExampleType { public ExampleType(string value) { } public static ExampleType Create() => new(\nstring.Empty\n); }")]
    [InlineData("public class ExampleType { public ExampleType() : this(\nstring.Empty\n) { } public ExampleType(string value) { } }")]
    [InlineData("[System.Obsolete(\n\"Message\"\n)] public class ExampleType { }")]
    [InlineData("public class ExampleType { public int Execute(int[] values) => values[\n0\n]; }")]
    [InlineData("public class ExampleType { public void Execute() { string.Concat(\"An intentionally long call argument that exceeds one hundred and twenty-eight characters together with\", \"the next argument it is joined to,\", \"and a third.\"); } }")]
    [InlineData("public class ExampleType { public void Execute() { System.Console.WriteLine(\n\"An intentionally long call argument that exceeds one hundred and twenty-eight characters, which a single item may do on one line.\"); } }")]
    [InlineData("public class ExampleType { public void Execute() { System.Console.WriteLine(\"An intentionally long call argument that exceeds one hundred and twenty-eight characters, which a single item may do on one line.\"\n); } }")]
    public async Task RejectsInvalidCallLayouts(string source)
    {
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source);

        Assert.Equal(ArgumentLayoutAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Rejects a later item indented other than one level past the line its list starts on, plus one more
    /// for each enclosing list that opens on that line and continues past it.
    /// </summary>
    /// <param name="source">A source with one list whose later items are indented wrongly.</param>
    [Theory]
    [InlineData("public class ExampleType { public void Execute() { string.Concat(\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\n      \"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\n    \"x\"); } }")]
    [InlineData("public class ExampleType { public void Execute() { string.Concat(\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\n\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\n    \"x\"); } }")]
    [InlineData("public class ExampleType { public void Execute(string value) { string.Concat(string.Concat(\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\n    \"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\n    \"x\"),\n    value,\n    value); } }")]
    [InlineData("public class ExampleType { public void Execute() { System.Console.WriteLine(string.Concat(\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\n        \"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\n        \"x\")); } }")]
    [InlineData("public class ExampleType { public void Execute() { string.Concat(\n// Explanation\n\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\n    \"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"); } }")]
    public async Task RejectsMisindentedItems(string source)
    {
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source);

        Assert.Equal(ArgumentLayoutAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Requires the first parameter beside the opening delimiter, each later one on a line of its own,
    /// and the closing delimiter after the last, for long lists.
    /// </summary>
    /// <param name="layout">A layout with a boundary that breaks or joins the wrong way.</param>
    /// <exception cref="ArgumentNullException">Thrown when a data row supplies no <paramref name="layout"/> to fill in.</exception>
    [Theory]
    [InlineData("FIRST, SECOND")]
    [InlineData("FIRST,\nSECOND\n")]
    [InlineData("\nFIRST, SECOND\n")]
    [InlineData("\nFIRST,\nSECOND")]
    [InlineData("\nFIRST,\nSECOND\n")]
    public async Task RejectsPartiallyExpandedLongLists(string layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        string parameters = layout.Replace(oldValue: "FIRST", "string " + new string(c: 'a', count: 70), StringComparison.Ordinal)
            .Replace(oldValue: "SECOND", "string " + new string(c: 'b', count: 70), StringComparison.Ordinal);

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(),
            $"public record ExampleType({parameters});");

        Assert.Equal(ArgumentLayoutAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Keeps statements written in sequence inside an argument on lines of their own, under a body that
    /// opens beneath its lambda, however short the list would be on one line (#38).
    /// </summary>
    /// <param name="source">A source whose one argument list holds statements laid out wrongly.</param>
    [Theory]
    [InlineData("public class ExampleType { public System.Threading.Tasks.Task Execute() => System.Threading.Tasks.Task.Run(() => { System.Console.WriteLine(); System.Console.WriteLine(); }); }")]
    [InlineData("public class ExampleType\n{\n    public System.Threading.Tasks.Task Execute() => System.Threading.Tasks.Task.Run(() =>\n        {\n            System.Console.WriteLine();\n            System.Console.WriteLine();\n        });\n}")]
    [InlineData("public class ExampleType { public System.Threading.Tasks.Task Execute() => System.Threading.Tasks.Task.Run(() => { if (System.Environment.ProcessorCount > 1) { System.Console.WriteLine(); return; } }); }")]
    [InlineData("public class ExampleType { public System.Threading.Tasks.Task Execute() => System.Threading.Tasks.Task.Run(() => { switch (System.Environment.ProcessorCount) { case 1: System.Console.WriteLine(); break; } }); }")]
    public async Task RejectsStatementsSharingALineInsideArguments(string source)
    {
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source);

        Assert.Equal(ArgumentLayoutAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Wraps a call's two arguments once something inside them would otherwise have to break: a chain, a call with
    /// three arguments, or an operator sequence that would end past the limit with the call on one line.
    /// </summary>
    /// <param name="second">The second argument of the call.</param>
    /// <exception cref="ArgumentNullException">Thrown when a data row supplies no <paramref name="second"/> argument to place in the call.</exception>
    [Theory]
    [InlineData("string.Concat(name, \"a suffix that makes this argument long enough\").Trim().ToUpperInvariant().TrimEnd()")]
    [InlineData("string.Concat(name, \"a first piece of text for this call\", \"and a second piece long enough to pass the limit\")")]
    [InlineData("name.Length > 3 ? name : \"a replacement long enough that this argument ends past the limit of its line\"")]
    public async Task WrapsShortCallsBeforeTheirArgumentsBreak(string second)
    {
        ArgumentNullException.ThrowIfNull(second);

        string wrapped = "public class ExampleType\n{\n    public bool Execute(string name)\n    {\n        return string.Equals(name.Trim().ToUpperInvariant().Normalize(),\n            "
            + second + ");\n    }\n}\n";

        Assert.Empty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), wrapped));

        string together = wrapped.Replace(oldValue: ",\n            ", newValue: ", ", StringComparison.Ordinal);

        // A call with three arguments inside reports as well until the call around it has wrapped.
        Assert.Contains(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), together),
            static diagnostic => diagnostic.Id == ArgumentLayoutAnalyzer.RuleIdentifier);
    }

    /// <summary>
    /// Wraps a call's two arguments like three when the second is a lambda, a string, or a call, which is large
    /// enough for a line of its own, and the line they are on would pass the limit.
    /// </summary>
    /// <param name="second">The second argument of the call.</param>
    /// <exception cref="ArgumentNullException">Thrown when a data row supplies no <paramref name="second"/> argument to place in the call.</exception>
    [Theory]
    [InlineData("\"and a second string\"")]
    [InlineData("$\"and a second string holding {first}\"")]
    [InlineData("static (string value) => value")]
    [InlineData("string.Concat(first, second)")]
    [InlineData("new object()")]
    public async Task WrapsTwoArgumentsWhoseSecondTakesALine(string second)
    {
        ArgumentNullException.ThrowIfNull(second);

        string wrapped = "public class ExampleType\n{\n    public void Execute(object first, object second)\n    {\n        Execute(\"a first argument long enough that this call ends past the one hundred and twenty-eight character limit of its line\",\n            "
            + second + ");\n    }\n}\n";

        Assert.Empty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), wrapped));

        string together = wrapped.Replace(oldValue: ",\n            ", newValue: ", ", StringComparison.Ordinal);

        Assert.Equal(ArgumentLayoutAnalyzer.RuleIdentifier,
            Assert.Single(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), together)).Id);
    }

    // What the analyzer decided before the bound existed: normalize the list and measure the result.
    private static bool ExceedsOneLine(string source)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();

        SyntaxNode list = Assert.Single(root.DescendantNodes()
            .Where(static node => node.IsKind(SyntaxKind.ParameterList))
            .ToArray());

        string compact = list.WithoutTrivia().NormalizeWhitespace(indentation: "", eol: " ").ToFullString();
        bool hasInlineComment = false;

        foreach (SyntaxTrivia trivia in list.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && list.Span.Contains(trivia.SpanStart))
                hasInlineComment = true;
        }

        return compact.Length - 2 > 128 || compact.IndexOfAny(anyOf: ['\r', '\n']) >= 0 || hasInlineComment;
    }
}
