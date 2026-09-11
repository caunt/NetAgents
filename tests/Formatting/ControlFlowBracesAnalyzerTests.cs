using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>
/// Verifies body-based brace requirements and exceptions that preserve scope and conditional binding.
/// </summary>
public sealed class ControlFlowBracesAnalyzerTests
{
    /// <summary>
    /// Verifies optional single-line braces and missing multiline braces are diagnosed.
    /// </summary>
    /// <param name="memberSource">A member containing exactly one brace violation.</param>
    [Theory]
    [InlineData("public static void Execute(int count) { if (count > 0) { return; } }")]
    [InlineData("public static void Execute(int count) { if (count >\n0) { return; } }")]
    [InlineData("public static void Execute(int count) { while (count > 0) { count--; } }")]
    [InlineData("public static void Execute(int count) { do { count--; } while (count > 0); }")]
    [InlineData("public static void Execute(int count) { for (int index = 0; index < count; index++) { System.Console.WriteLine(index); } }")]
    [InlineData("public static void Execute(int[] values) { foreach (int value in values) { System.Console.WriteLine(value); } }")]
    [InlineData("public static void Execute(int[] values) { foreach (int value\nin values) { System.Console.WriteLine(value); } }")]
    [InlineData("public static void Execute((int, int)[] values) { foreach (var (first, second) in values) { System.Console.WriteLine(first + second); } }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute(System.Collections.Generic.IAsyncEnumerable<int> values) { await foreach (int value in values) { System.Console.WriteLine(value); } }")]
    [InlineData("public static System.Collections.Generic.IEnumerable<int> Execute(int[] values) { foreach (int value in values) { yield return value; } }")]
    [InlineData("public static void Execute() { using (var stream = new System.IO.MemoryStream()) { stream.Flush(); } }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { await using (var stream = new System.IO.MemoryStream()) { await stream.FlushAsync(); } }")]
    [InlineData("public static unsafe void Execute(int[] values) { fixed (int* pointer = values) { *pointer = 0; } }")]
    [InlineData("public static void Execute(string value) { lock (value) { System.Console.WriteLine(value); } }")]
    [InlineData("public static int Execute(int count) { switch (count) { default: { return count; } } }")]
    [InlineData("public static void Execute() { { return; } }")]
    [InlineData("public static void Execute() { destination: { return; } }")]
    [InlineData("public static void Execute(int count) { if (count > 0) System.Console.WriteLine(\ncount); }")]
    [InlineData("public static void Execute(int count) { while (count > 0) count = System.Math.Abs(\ncount - 1); }")]
    [InlineData("public static void Execute(int count) { do count = System.Math.Abs(\ncount - 1); while (count > 0); }")]
    [InlineData("public static void Execute(int count) { for (int index = 0; index < count; index++) System.Console.WriteLine(\nindex); }")]
    [InlineData("public static void Execute(int[] values) { foreach (int value in values) System.Console.WriteLine(\nvalue); }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute(System.Collections.Generic.IAsyncEnumerable<int> values) { await foreach (int value in values) System.Console.WriteLine(\nvalue); }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { await using (var stream = new System.IO.MemoryStream()) stream.WriteByte(\n0); }")]
    [InlineData("public static void Execute() { using (var stream = new System.IO.MemoryStream()) stream.WriteByte(\n0); }")]
    [InlineData("public static unsafe void Execute(int[] values) { fixed (int* pointer = values) *pointer =\n0; }")]
    [InlineData("public static int Execute(int count) { switch (count) { default: return System.Math.Abs(\ncount); } }")]
    [InlineData("public static int Execute(int count) { switch (count) { default: count++; return count; } }")]
    [InlineData("public static int Execute(int count) { destination: return System.Math.Abs(\ncount); }")]
    public async Task RequiresMatchingBodyBraces(string memberSource)
    {
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ControlFlowBracesAnalyzer(),
            $"public static class ExampleType {{ {memberSource} }}", allowUnsafeCode: true);

        Assert.Equal(ControlFlowBracesAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Verifies required braces, multiline bodies, and unsafe scope changes remain untouched.
    /// </summary>
    /// <param name="memberSource">A member whose braces must remain as written.</param>
    [Theory]
    [InlineData("public static int Execute() { return 0; }")]
    [InlineData("public static int Result { get { return 0; } }")]
    [InlineData("public static void Execute() { System.Action action = () => { System.Console.WriteLine(); }; action(); }")]
    [InlineData("public static void Execute() { void Local() { System.Console.WriteLine(); } Local(); }")]
    [InlineData("public static void Execute(int count) { if (count > 0) { } }")]
    [InlineData("public static void Execute(int count) { if (count > 0) { count++; count--; } }")]
    [InlineData("public static void Execute(int count) { if (count > 0) { count = System.Math.Abs(count)\n.GetHashCode(); } }")]
    [InlineData("public static void Execute(int count) { foreach (int value in new[] { count }) { System.Console.WriteLine(\nvalue); } }")]
    [InlineData("public static void Execute(int count) { if (count > 0) { int result = 0; } }")]
    [InlineData("public static void Execute(int count) { if (count > 0) { void Local() { } } }")]
    [InlineData("public static void Execute(int count) { if (count > 0) { destination: return; } }")]
    [InlineData("public static void Execute(string value) { if (value.Length > 0) { System.Console.WriteLine(int.TryParse(value, out int result)); } }")]
    [InlineData("public static void Execute(string value) { if (value.Length > 0) { System.Console.WriteLine(value is { Length: > 0 } result); } }")]
    [InlineData("public static void Execute() { try { return; } catch (System.Exception) { throw; } finally { System.Console.WriteLine(); } }")]
    [InlineData("public static void Execute(int count) { checked { count++; } unchecked { count++; } }")]
    [InlineData("public static unsafe void Execute(int* pointer) { unsafe { *pointer = 0; } }")]
    [InlineData("public static void Execute(int count) { if (count > 0) { if (count > 1) return; } else { return; } }")]
    [InlineData("public static void Execute(int count) { if (count > 0) while (count > 1) { if (count > 2) return; } else return; }")]
    [InlineData("public static void Execute(int count) { if (count > 0) { count = System.Math.Abs(\ncount); } else if (count < 0) { return; } else { return; } }")]
    [InlineData("public static void Execute(int count) { if (count > 0) { return; } else { count = System.Math.Abs(\ncount); } }")]
    [InlineData("public static int Execute(int count) { switch (count) { case 0: int result; goto default; default: return result = 1; } }")]
    [InlineData("public static int Execute(int count) { switch (count) { case 0: int Local<TValue>() => 1; return Local<int>(); default: return Local<string>(); } }")]
    [InlineData("public static int Execute(int count) { switch (count) { case 0: count++; destination: return count; default: goto destination; } }")]
    [InlineData("public static void Execute(int count) { if (count > 0) {\n#if true\nreturn;\n#endif\n} }")]
    public async Task PreservesRequiredBracesAndScopes(string memberSource)
    {
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ControlFlowBracesAnalyzer(),
            $"public static class ExampleType {{ {memberSource} }}", allowUnsafeCode: true);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies all simple branches lose braces, while mixed chains acquire braces consistently.
    /// </summary>
    /// <param name="statements">The conditional chain.</param>
    /// <param name="expectedCount">The number of bodies needing a change.</param>
    [Theory]
    [InlineData("if (count > 0) { return; } else if (count < 0) { return; } else { return; }", "3")]
    [InlineData("if (count > 0) System.Console.WriteLine(\ncount); else if (count < 0) return; else return;", "3")]
    [InlineData("if (count > 0) { System.Console.WriteLine(\ncount); } else return;", "1")]
    [InlineData("if (count > 0) return; else { System.Console.WriteLine(\ncount); }", "1")]
    [InlineData("if (count > 0) { count++; count--; } else return;", "1")]
    public async Task KeepsConditionalChainsConsistent(string statements, string expectedCount)
    {
        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new ControlFlowBracesAnalyzer(),
            $"public static class ExampleType {{ public static void Execute(int count) {{ {statements} }} }}");

        Assert.Equal(int.Parse(expectedCount, System.Globalization.CultureInfo.InvariantCulture), diagnostics.Length);
        Assert.All(diagnostics, static diagnostic => Assert.Equal(ControlFlowBracesAnalyzer.RuleIdentifier, diagnostic.Id));
    }
}
