using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>
/// Verifies argument and parameter layout for declarations and calls.
/// </summary>
public sealed class ArgumentLayoutAnalyzerTests
{
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
    /// Uses compact parameter text, excluding delimiters and the declaration prefix.
    /// </summary>
    /// <param name="lengthText">The exact compact parameter length.</param>
    [Theory]
    [InlineData("127")]
    [InlineData("128")]
    [InlineData("129")]
    public async Task EnforcesExactBoundary(string lengthText)
    {
        int length = int.Parse(lengthText, System.Globalization.CultureInfo.InvariantCulture);
        string parameter = "string " + new string(c: 'p', length - 7);
        string source = $"public class ExampleType {{ public void VeryLongMethodNameThatDoesNotCountTowardsTheLimit({parameter}) {{ }} }}";
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source);

        Assert.Equal(length > 128 ? 1 : 0, diagnostics.Length);

        string expanded = $"public class ExampleType {{ public void Execute(\n    {parameter}\n) {{ }} }}";
        Diagnostic[] expandedDiagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), expanded);

        Assert.Equal(length > 128 ? 0 : 1, expandedDiagnostics.Length);
    }

    /// <summary>
    /// Requires every parameter and the closing delimiter on separate lines for long lists.
    /// </summary>
    /// <param name="layout">A layout with a boundary missing a newline.</param>
    [Theory]
    [InlineData("FIRST, SECOND")]
    [InlineData("FIRST,\nSECOND\n")]
    [InlineData("\nFIRST, SECOND\n")]
    [InlineData("\nFIRST,\nSECOND")]
    public async Task RejectsPartiallyExpandedLongLists(string layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        string parameters = layout.Replace(oldValue: "FIRST", "string " + new string(c: 'a', count: 70), StringComparison.Ordinal)
            .Replace(oldValue: "SECOND", "string " + new string(c: 'b', count: 70), StringComparison.Ordinal);

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), $"public record ExampleType({parameters});");

        Assert.Equal(ArgumentLayoutAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Rejects incorrectly wrapped calls as well as declarations.
    /// </summary>
    /// <param name="source">A source with one incorrectly laid out argument list.</param>
    [Theory]
    [InlineData("public class ExampleType { public void Execute(string value, int count) { Execute(\nvalue,\ncount\n); } }")]
    [InlineData(
        "public class ExampleType { public ExampleType(string value) { } public static ExampleType Create() => new ExampleType(\nstring.Empty\n); }"
    )]
    [InlineData("public class ExampleType { public ExampleType(string value) { } public static ExampleType Create() => new(\nstring.Empty\n); }")]
    [InlineData("public class ExampleType { public ExampleType() : this(\nstring.Empty\n) { } public ExampleType(string value) { } }")]
    [InlineData("[System.Obsolete(\n\"Message\"\n)] public class ExampleType { }")]
    [InlineData("public class ExampleType { public int Execute(int[] values) => values[\n0\n]; }")]
    [InlineData(
        "public class ExampleType { public void Execute() { System.Console.WriteLine(\"An intentionally long call argument that exceeds one hundred and twenty-eight characters and must therefore be expanded by the argument formatting rule.\"); } }"
    )]
    public async Task RejectsInvalidCallLayouts(string source)
    {
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source);

        Assert.Equal(ArgumentLayoutAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Accepts compact lists and expanded lists whose content cannot safely occupy one line.
    /// </summary>
    /// <param name="source">The correctly laid out source.</param>
    [Theory]
    [InlineData("public record ExampleType(string Value, int Count);")]
    [InlineData("public class ExampleType { public void Execute(string value, int count) { Execute(value, count); } }")]
    [InlineData("public class ExampleType { public void Execute(\n// Explanation\nstring value\n) { } }")]
    [InlineData("public class ExampleType { public void Execute(\n/* Explanation\ncontinued */ string value\n) { } }")]
    public async Task AcceptsValidLayouts(string source)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source));
    }

    /// <summary>
    /// Counts call argument contents without the invocation name or parentheses.
    /// </summary>
    /// <param name="lengthText">The compact argument length, including quotes.</param>
    [Theory]
    [InlineData("127")]
    [InlineData("128")]
    [InlineData("129")]
    public async Task EnforcesCallBoundary(string lengthText)
    {
        int length = int.Parse(lengthText, System.Globalization.CultureInfo.InvariantCulture);
        string argument = "\"" + new string(c: 'x', length - 2) + "\"";
        string source = $"public class ExampleType {{ public void Execute() {{ System.Console.WriteLine({argument}); }} }}";
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), source);

        Assert.Equal(length > 128 ? 1 : 0, diagnostics.Length);
    }
}
