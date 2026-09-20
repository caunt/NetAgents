using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.Analyzers.TypeSafety;

namespace NetAgents.Analyzers.Tests.TypeSafety;

/// <summary>
/// Covers explicit and inferred values that lose static type information.
/// </summary>
public sealed class UntypedValueAnalyzerTests
{
    /// <summary>
    /// Verifies that framework-prescribed signatures may keep the object parameters and results they inherit.
    /// </summary>
    /// <param name="source">A type whose object parameters and results are inherited.</param>
    [Theory]
    [InlineData("public class ExampleType { public override bool Equals(object? other) => false; public override int GetHashCode() => 0; }")]
    [InlineData("public class ExampleType : System.IComparable { int System.IComparable.CompareTo(object? other) => 0; }")]
    [InlineData(
        "public class ExampleType : System.Collections.IEnumerator { object System.Collections.IEnumerator.Current => this; bool System.Collections.IEnumerator.MoveNext() => false; void System.Collections.IEnumerator.Reset() { } }"
    )]
    public async Task AllowsFrameworkPrescribedSignatures(string source)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new UntypedValueAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies that generic collections retain their element types.
    /// </summary>
    [Fact]
    public async Task AllowsStronglyTypedGenericCollections()
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(
            new UntypedValueAnalyzer(),
            source: "public class ExampleType { public System.Collections.Generic.List<string> Values { get; } = new(); }"
        );

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies that untyped declarations and inferred framework results are reported.
    /// </summary>
    /// <param name="memberSource">The member containing an untyped value.</param>
    [Theory]
    [InlineData("public object? Value { get; set; }")]
    [InlineData("public System.Object? Value { get; set; }")]
    [InlineData("public dynamic? Value { get; set; }")]
    [InlineData("public System.Collections.Generic.List<object> Values { get; } = new();")]
    [InlineData("public void ReadValue() { var value = System.AppContext.GetData(\"setting\"); }")]
    [InlineData("public void ReadValue() { System.Collections.ArrayList values = new(); var value = values[0]; }")]
    [InlineData("public bool Check(object? other) => false;")]
    [InlineData("public override bool Equals(object? other) { object copy = other!; return copy is null; }")]
    public async Task ReportsUntypedDeclarationsAndInferredValues(string memberSource)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new UntypedValueAnalyzer(), $"public class ExampleType {{ {memberSource} }}");

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == UntypedValueAnalyzer.RuleIdentifier);
    }
}
