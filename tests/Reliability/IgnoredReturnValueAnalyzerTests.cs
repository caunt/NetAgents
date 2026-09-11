using NetAgents.Analyzers.Reliability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Reliability;

/// <summary>
/// Covers ignored synchronous and asynchronous results and meaningful consumption.
/// </summary>
public sealed class IgnoredReturnValueAnalyzerTests
{
    /// <summary>
    /// Verifies that statements, callbacks, loops, and discards cannot drop returned values.
    /// </summary>
    /// <param name="memberSource">The member discarding a returned value.</param>
    [Theory]
    [InlineData("public static void Execute(System.Collections.Generic.Dictionary<string, string> values, string key) { values.TryGetValue(key, out var value); System.Console.WriteLine(value); }")]
    [InlineData("public static void Execute(System.Collections.Generic.Dictionary<string, string> values, string key) { _ = values.TryGetValue(key, out var value); System.Console.WriteLine(value); }")]
    [InlineData("public static void Execute(string text) { int.TryParse(text, out int value); System.Console.WriteLine(value); }")]
    [InlineData("public static void Execute(string text) { _ = int.TryParse(text, out int value); System.Console.WriteLine(value); }")]
    [InlineData("public static void Execute(string text) { _ = (int.TryParse(text, out int value)); }")]
    [InlineData("public static void Execute(string text) { text.Trim(); }")]
    [InlineData("public static void Execute(string? text) { text?.Trim(); }")]
    [InlineData("public static void Execute(string? text) { _ = text?.Trim(); }")]
    [InlineData("public static void Execute(string text) => text.Trim();")]
    [InlineData("public static System.Action CreateCallback(string text) => () => text.Trim();")]
    [InlineData("public static System.Action CreateCallback(string text) => delegate { text.Trim(); };")]
    [InlineData("public static void Execute(string text) { void Complete() => text.Trim(); Complete(); }")]
    [InlineData("public static void Execute(System.Func<bool> callback) { callback(); }")]
    [InlineData("public static void Execute(System.Func<bool>? callback) { callback?.Invoke(); }")]
    [InlineData("public static void Execute(string text) { for (text.Trim(); text.Length > 0;) { break; } }")]
    [InlineData("public static void Execute(string text) { for (; text.Length > 0; text.Trim()) { break; } }")]
    [InlineData("public static void Execute() { System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false); }")]
    [InlineData("public static void Execute() { System.Threading.Tasks.Task.Delay(1); }")]
    [InlineData("public static void Execute() { _ = System.Threading.Tasks.Task.Delay(1); }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { await System.Threading.Tasks.Task.FromResult(true); }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { _ = await System.Threading.Tasks.Task.FromResult(true); }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() => await System.Threading.Tasks.Task.FromResult(true);")]
    [InlineData("public static System.Func<System.Threading.Tasks.Task> CreateCallback() => async () => await System.Threading.Tasks.Task.FromResult(true);")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { await System.Threading.Tasks.Task.FromResult(true).ConfigureAwait(false); }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { await new System.Threading.Tasks.ValueTask<int>(1); }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { await new System.Threading.Tasks.ValueTask<int>(1).ConfigureAwait(false); }")]
    [InlineData("public static void Execute(string text) { bool parsed = int.TryParse(text, out int value); _ = parsed; }")]
    [InlineData("public static void Execute(string text) { _ = text.Trim().Length > 0; }")]
    [InlineData("public static void Execute(System.Text.StringBuilder builder) { builder.AppendLine(); }")]
    [InlineData("public static unsafe void Execute(delegate*<bool> callback) { callback(); }")]
    [InlineData("public static void Execute(System.Func<(int, int)> callback) { var (number, _) = callback(); System.Console.WriteLine(number); }")]
    [InlineData("public static void Execute(System.Func<(int, int)> callback) { (_, _) = callback(); }")]
    [InlineData("public static void Execute(System.Func<(int, (int, int))> callback) { var (number, (otherNumber, _)) = callback(); System.Console.WriteLine(number + otherNumber); }")]
    public async Task RejectsIgnoredAndDiscardedValues(string memberSource)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new IgnoredReturnValueAnalyzer(), $"public static class ExampleType {{ {memberSource} }}", allowUnsafeCode: true);

        Assert.Equal(IgnoredReturnValueAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Verifies that consuming a result or awaiting completion without a result is permitted.
    /// </summary>
    /// <param name="memberSource">The member consuming returned values.</param>
    [Theory]
    [InlineData("public static void Execute(System.Collections.Generic.Dictionary<string, string> values, string key) { if (values.TryGetValue(key, out var value)) { System.Console.WriteLine(value); } }")]
    [InlineData("public static void Execute(string text) { if (int.TryParse(text, out int value)) { System.Console.WriteLine(value); } }")]
    [InlineData("public static bool Execute(string text) => int.TryParse(text, out _);")]
    [InlineData("public static bool Execute(string text) { bool parsed = int.TryParse(text, out int value); System.Console.WriteLine(value); return parsed; }")]
    [InlineData("public static void Execute(string text) { System.Console.WriteLine(int.TryParse(text, out int value)); }")]
    [InlineData("public static string Execute(string text) => text.Trim();")]
    [InlineData("public static string? Execute(string? text) => text?.Trim();")]
    [InlineData("public static System.Func<string> CreateCallback(string text) => () => text.Trim();")]
    [InlineData("public static System.Action CreateCallback(string text) => () => System.Console.WriteLine(text);")]
    [InlineData("public static void Execute(System.Action? callback) { callback?.Invoke(); }")]
    [InlineData("public static void Execute(System.Func<bool> callback) { for (; callback();) { } }")]
    [InlineData("public static void Execute(string text) { for (int index = 0; index < text.Length; index++) { System.Console.WriteLine(index); } }")]
    [InlineData("public static void Execute(string text) { for (text = text.Trim(); text.Length > 0; text = text.Substring(1)) { } }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { await System.Threading.Tasks.Task.Delay(1); }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false); }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { await new System.Threading.Tasks.ValueTask().ConfigureAwait(false); }")]
    [InlineData("public static async System.Threading.Tasks.Task<bool> Execute() => await System.Threading.Tasks.Task.FromResult(true);")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { if (await System.Threading.Tasks.Task.FromResult(true)) { System.Console.WriteLine(true); } }")]
    [InlineData("public static async System.Threading.Tasks.Task Execute() { int value = await new System.Threading.Tasks.ValueTask<int>(1); System.Console.WriteLine(value); }")]
    [InlineData("public static System.Text.StringBuilder Execute(System.Text.StringBuilder builder) => builder.AppendLine();")]
    [InlineData("public static unsafe bool Execute(delegate*<bool> callback) => callback();")]
    [InlineData("public static unsafe void Execute(delegate*<void> callback) { callback(); }")]
    [InlineData("public static int Execute(System.Func<(int, int)> callback) { var (first, second) = callback(); return first + second; }")]
    [InlineData("public static int Execute(System.Func<(int, (int, int))> callback) { var (first, (second, third)) = callback(); return first + second + third; }")]
    [InlineData("public static void Execute(System.Text.StringBuilder? builder) { builder?.Length = 0; }")]
    public async Task AllowsConsumedValuesAndVoidCalls(string memberSource)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new IgnoredReturnValueAnalyzer(), $"public static class ExampleType {{ {memberSource} }}", allowUnsafeCode: true);

        Assert.Empty(diagnostics);
    }
}
