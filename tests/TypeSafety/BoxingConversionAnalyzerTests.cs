using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.Analyzers.TypeSafety;

namespace NetAgents.Analyzers.Tests.TypeSafety;

/// <summary>
/// Covers boxing conversions and strongly typed alternatives.
/// </summary>
public sealed class BoxingConversionAnalyzerTests
{
    /// <summary>
    /// Verifies that nullable, interface, generic, and explicit boxing are reported.
    /// </summary>
    /// <param name="memberSource">The member containing a boxing conversion.</param>
    [Theory]
    [InlineData("public static object ConvertValue(int value) => value;")]
    [InlineData("public static object ConvertValue(int? value) => value!;")]
    [InlineData("public static System.IComparable ConvertValue(int value) => value;")]
    [InlineData("public static object ConvertValue<TValue>(TValue value) => value!;")]
    [InlineData("public static object ConvertValue(System.DayOfWeek value) => value;")]
    [InlineData("public static string FormatValue(int value) => string.Format(\"{0}\", value);")]
    public async Task ReportsExplicitAndImplicitBoxing(string memberSource)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new BoxingConversionAnalyzer(), $"public static class ExampleType {{ {memberSource} }}");

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == BoxingConversionAnalyzer.RuleIdentifier);
    }

    /// <summary>
    /// Verifies typed operations, interpolation, and constrained generic calls.
    /// </summary>
    /// <param name="memberSource">The strongly typed member to analyze.</param>
    [Theory]
    [InlineData("public static int ConvertValue(int value) => value;")]
    [InlineData("public static string FormatValue(int value) => $\"{value}\";")]
    [InlineData("public static string FormatValue<TValue>(TValue value) where TValue : System.IFormattable => value.ToString(null, null);")]
    public async Task AllowsTypedAndConstrainedGenericOperations(string memberSource)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new BoxingConversionAnalyzer(), $"public static class ExampleType {{ {memberSource} }}");

        Assert.Empty(diagnostics);
    }
}
