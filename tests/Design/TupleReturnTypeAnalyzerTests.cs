using NetAgents.Analyzers.Design;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Design;

/// <summary>
/// Covers tuple result contracts and tuple use outside result positions.
/// </summary>
public sealed class TupleReturnTypeAnalyzerTests
{
    /// <summary>
    /// Verifies tuples remain available for inputs, storage, deconstruction, and dependency results.
    /// </summary>
    /// <param name="memberSource">The member using tuples without returning one.</param>
    [Theory]
    [InlineData("private static (int First, int Second) _pair = (1, 2); public static int Execute() => _pair.First + _pair.Second;")]
    [InlineData("public static int Execute((int First, int Second) pair) => pair.First + pair.Second;")]
    [InlineData("public static int Execute() { (int First, int Second) pair = (1, 2); return pair.First + pair.Second; }")]
    [InlineData("public static int Execute() { var (first, second) = (1, 2); return first + second; }")]
    [InlineData(
        "public static int Execute(System.Func<(int First, int Second)> callback) { var pair = callback(); return pair.First + pair.Second; }"
    )]
    [InlineData("public static NamedResult Execute() => new(1, 2); public sealed record NamedResult(int First, int Second);")]
    [InlineData(
        "public static System.Threading.Tasks.Task<NamedResult> Execute() => System.Threading.Tasks.Task.FromResult(new NamedResult(1, 2)); public sealed record NamedResult(int First, int Second);"
    )]
    public async Task AllowsTuplesOutsideResultContracts(string memberSource)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new TupleReturnTypeAnalyzer(), $"public static class ExampleType {{ {memberSource} }}");

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies inherited tuple contracts are diagnosed at their authoring declaration, not again at implementations.
    /// </summary>
    [Fact]
    public async Task ExemptsPrescribedInterfaceAndOverrideResults()
    {
        const string source = """
            public interface IExample
            {
                (int First, int Second) Read();
            }

            public class BaseExample
            {
                public virtual (int First, int Second) Read() => (1, 2);
            }

            public sealed class DerivedExample : BaseExample, IExample
            {
                public override (int First, int Second) Read() => (1, 2);
            }

            public sealed class ExplicitExample : IExample
            {
                (int First, int Second) IExample.Read() => (1, 2);
            }

            public sealed class ImplicitExample : IExample
            {
                public (int First, int Second) Read() => (1, 2);
            }
            """;

        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new TupleReturnTypeAnalyzer(), source);

        Assert.Equal(expected: 2, diagnostics.Length);
        Assert.All(diagnostics, static diagnostic => Assert.Equal(TupleReturnTypeAnalyzer.RuleIdentifier, diagnostic.Id));
    }

    /// <summary>
    /// Verifies an inline function-pointer contract cannot produce a tuple.
    /// </summary>
    [Fact]
    public async Task RejectsTupleFunctionPointerResults()
    {
        const string source = "public static unsafe class ExampleType { public static delegate*<(int First, int Second)> Callback; }";

        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new TupleReturnTypeAnalyzer(), source, allowUnsafeCode: true);

        Assert.Equal(TupleReturnTypeAnalyzer.RuleIdentifier, Assert.Single(diagnostics).Id);
    }

    /// <summary>
    /// Verifies each tuple representation and nested result shape is rejected.
    /// </summary>
    /// <param name="source">The source declaring a tuple result.</param>
    [Theory]
    [InlineData("public static class ExampleType { public static (int First, int Second) Execute() => (1, 2); }")]
    [InlineData("public static class ExampleType { public static System.ValueTuple Execute() => default; }")]
    [InlineData("public static class ExampleType { public static System.ValueTuple<int, int> Execute() => new(1, 2); }")]
    [InlineData("public static class ExampleType { public static System.Tuple<int, int> Execute() => System.Tuple.Create(1, 2); }")]
    [InlineData(
        "public static class ExampleType { public static System.Threading.Tasks.Task<(int First, int Second)> Execute() => System.Threading.Tasks.Task.FromResult((1, 2)); }"
    )]
    [InlineData(
        "public static class ExampleType { public static System.Threading.Tasks.ValueTask<System.Tuple<int, int>> Execute() => new(System.Tuple.Create(1, 2)); }"
    )]
    [InlineData(
        "public static class ExampleType { public static System.Threading.Tasks.ValueTask<System.ValueTuple> Execute() => new(default(System.ValueTuple)); }"
    )]
    [InlineData("public static class ExampleType { public static System.Collections.Generic.List<(int First, int Second)> Execute() => []; }")]
    [InlineData("using Pair = System.ValueTuple<int, int>; public static class ExampleType { public static Pair Execute() => new(1, 2); }")]
    [InlineData("public static class ExampleType { public static (int First, int Second)? Execute() => (1, 2); }")]
    [InlineData("public sealed class ExampleType { public (int First, int Second) Value => (1, 2); }")]
    [InlineData("public sealed class ExampleType { public (int First, int Second) this[int index] => (index, index); }")]
    [InlineData("public delegate (int First, int Second) ExampleType();")]
    [InlineData(
        "public readonly struct ExampleType { public static (int First, int Second) operator +(ExampleType left, ExampleType right) => (1, 2); }"
    )]
    [InlineData(
        "public static class ExampleType { public static int Execute() { (int First, int Second) Local() => (1, 2); return Local().First; } }"
    )]
    [InlineData(
        "public static class ExampleType { public static int Execute() { System.Func<(int First, int Second)> callback = () => (1, 2); return callback().First; } }"
    )]
    [InlineData(
        "public static class ExampleType { public static int Execute() { System.Func<System.Tuple<int, int>> callback = delegate { return System.Tuple.Create(1, 2); }; return callback().Item1; } }"
    )]
    public async Task RejectsTupleResultContracts(string source)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new TupleReturnTypeAnalyzer(), source);
        Microsoft.CodeAnalysis.Diagnostic diagnostic = Assert.Single(diagnostics);

        Assert.Equal(TupleReturnTypeAnalyzer.RuleIdentifier, diagnostic.Id);
        Assert.Contains(
            expectedSubstring: "dedicated named",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal
        );
    }
}
