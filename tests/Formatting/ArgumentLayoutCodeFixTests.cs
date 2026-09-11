using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>
/// Verifies fixes preserve parameter syntax and format invocation arguments consistently.
/// </summary>
public sealed class ArgumentLayoutCodeFixTests
{
    /// <summary>
    /// Collapses short parameter declarations, including attributes and default values.
    /// </summary>
    [Fact]
    public async Task CollapsesShortSignaturesAndCalls()
    {
        const string source = "public class ExampleType { public void Execute(\n[System.Runtime.InteropServices.In] string value,\nint count = 0\n) { Execute(\nvalue,\ncount\n); } }";
        const string expected = "public class ExampleType { public void Execute([System.Runtime.InteropServices.In] string value, int count = 0) { Execute(value, count); } }";

        Assert.Equal(expected, await CodeFixTestHarness.FixArguments(source, fixAll: true));
    }

    /// <summary>
    /// Expands a long record signature while retaining the document's newline convention.
    /// </summary>
    /// <param name="newline">The document's line ending.</param>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task ExpandsLongRecordParameters(string newline)
    {
        string first = "string " + new string(c: 'a', count: 70);
        string second = "int " + new string(c: 'b', count: 70);
        string source = $"// Record{newline}public record ExampleType({first}, {second});{newline}";
        string expected = $"// Record{newline}public record ExampleType({newline}    {first},{newline}    {second}{newline});{newline}";

        Assert.Equal(expected, await CodeFixTestHarness.FixArguments(source, fixAll: true));
    }

    /// <summary>
    /// Fix all handles several declarations with different target layouts.
    /// </summary>
    [Fact]
    public async Task FixesMixedLayoutsInOneDocument()
    {
        string parameter = "string " + new string(c: 'p', count: 122);
        string source = $"public class ExampleType\n{{\n    public ExampleType({parameter}) {{ }}\n    public void Execute(\nint value\n) {{ }}\n}}";
        string expected = $"public class ExampleType\n{{\n    public ExampleType(\n        {parameter}\n    ) {{ }}\n    public void Execute(int value) {{ }}\n}}";

        Assert.Equal(expected, await CodeFixTestHarness.FixArguments(source, fixAll: true));
    }

    /// <summary>
    /// Keeps comments while expanding a list that cannot safely be collapsed.
    /// </summary>
    [Fact]
    public async Task PreservesLineComments()
    {
        const string source = "public class ExampleType { public void Execute(int first, int second) { Execute(1, // explanation\n2); } }";
        const string expected = "public class ExampleType { public void Execute(int first, int second) { Execute(\n    1,\n    // explanation\n    2\n); } }";

        Assert.Equal(expected, await CodeFixTestHarness.FixArguments(source, fixAll: true));
    }

    /// <summary>
    /// Keeps multiline raw string tokens byte-for-byte while moving list delimiters.
    /// </summary>
    [Fact]
    public async Task PreservesMultilineLiteralContent()
    {
        const string literal = "\"\"\"\nfirst line\nsecond line\n\"\"\"";
        string source = $"public class ExampleType {{ public void Execute() {{ System.Console.WriteLine({literal}); }} }}";
        string expected = $"public class ExampleType {{ public void Execute() {{ System.Console.WriteLine(\n    {literal}\n); }} }}";

        Assert.Equal(expected, await CodeFixTestHarness.FixArguments(source, fixAll: true));
    }

    /// <summary>
    /// Fix all resolves nested call lists without conflicting text edits.
    /// </summary>
    [Fact]
    public async Task FixesNestedCalls()
    {
        string argument = "\"" + new string(c: 'x', count: 130) + "\"";
        string source = $"public class ExampleType {{ public void Execute() {{ System.Console.WriteLine(string.Concat({argument}, {argument})); }} }}";
        string expected = $"public class ExampleType {{ public void Execute() {{ System.Console.WriteLine(\n    string.Concat(\n        {argument},\n        {argument}\n    )\n); }} }}";

        Assert.Equal(expected, await CodeFixTestHarness.FixArguments(source, fixAll: true));
    }
    /// <summary>
    /// An individual fix collapses a call without requiring Fix all.
    /// </summary>
    [Fact]
    public async Task FixesIndividualCall()
    {
        const string source = "public class ExampleType { public void Execute() { System.Console.WriteLine(\n1\n); } }";
        const string expected = "public class ExampleType { public void Execute() { System.Console.WriteLine(1); } }";

        Assert.Equal(expected, await CodeFixTestHarness.FixArguments(source, fixAll: false));
    }

    /// <summary>
    /// Collapsing a short lambda argument preserves its body and reaches a stable layout.
    /// </summary>
    [Fact]
    public async Task CollapsesLambdaArgument()
    {
        const string source = "public class ExampleType { public System.Threading.Tasks.Task Execute() => System.Threading.Tasks.Task.Run(\n() => {\nSystem.Console.WriteLine();\n}\n); }";
        const string expected = "public class ExampleType { public System.Threading.Tasks.Task Execute() => System.Threading.Tasks.Task.Run(() => { System.Console.WriteLine(); }); }";

        Assert.Equal(expected, await CodeFixTestHarness.FixArguments(source, fixAll: true));
    }
}
