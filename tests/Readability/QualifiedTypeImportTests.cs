using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Readability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

/// <summary>
/// Covers the qualified type references that become imports, and the ones that cannot.
/// </summary>
public sealed class QualifiedTypeImportTests
{
    /// <summary>
    /// Verifies that a reference the language requires to stay qualified is accepted.
    /// </summary>
    /// <param name="body">The member body holding the reference.</param>
    [Theory]
    [InlineData("string Run() => nameof(System.Text.StringBuilder);")]
    [InlineData("string Run() => typeof(Outer.Inner).Name;")]
    [InlineData("string Run() => nameof(System.Math.Min);")]
    [InlineData("int Run(int first, int second) => global::System.Math.Min(first, second);")]
    [InlineData("int Run(string text) => text.Length;")]
    [InlineData("string Run() => string.Empty;")]
    [InlineData("int Run() => Outer.Count;")]
    public async Task AllowsReferencesThatCannotBeImported(string body)
    {
        string source = $$"""
            class Outer
            {
                internal const int Count = 1;

                internal sealed class Inner;
            }

            class ExampleType
            {
                {{body}}
            }
            """;

        Assert.Empty(await AnalyzerTestHarness.Analyze(new QualifiedTypeImportAnalyzer(), source));
    }

    /// <summary>
    /// Verifies that the import is placed with the other System directives and the name is shortened.
    /// </summary>
    [Fact]
    public async Task ImportsAndShortensTheReference()
    {
        const string source = """
            using System;

            class ExampleType
            {
                System.Text.StringBuilder Run() => new();
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixQualifiedTypeImports(source, fixAll: true);

        Assert.Equal(expected: """
            using System;
            using System.Text;

            class ExampleType
            {
                StringBuilder Run() => new();
            }
            """,
            fixedSource);
    }

    /// <summary>
    /// Verifies that a reference qualified only by the enclosing namespace is imported in full.
    /// </summary>
    [Fact]
    public async Task ImportsPartiallyQualifiedReferences()
    {
        const string source = """
            namespace Company.Features
            {
                internal sealed class Widget;
            }

            namespace Company
            {
                class ExampleType
                {
                    Features.Widget Run() => new();
                }
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixQualifiedTypeImports(source, fixAll: true);

        Assert.Contains(expectedSubstring: "using Company.Features;", fixedSource, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "Widget Run()", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "Features.Widget", fixedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a type reached through its namespace in an expression, as the start of a call, a
    /// static member, an enum value, or a nested type, is imported and shortened like a type name.
    /// </summary>
    /// <param name="body">The member as written, in a file importing nothing.</param>
    /// <param name="expected">The member after the fix.</param>
    /// <param name="directives">The using directives the fix adds, each ending its own line.</param>
    [Theory]
    [InlineData("int Run(int first, int second) => System.Math.Min(first, second);",
        "int Run(int first, int second) => Math.Min(first, second);",
        "using System;\n")]
    [InlineData("int Run(int first, int second) => System.Math.Max(System.Math.Min(first, second), 0);",
        "int Run(int first, int second) => Math.Max(Math.Min(first, second), 0);",
        "using System;\n")]
    [InlineData("bool Run(string text) => text.StartsWith(\"a\", System.StringComparison.Ordinal);",
        "bool Run(string text) => text.StartsWith(\"a\", StringComparison.Ordinal);",
        "using System;\n")]
    [InlineData("bool Run(int value) => System.Collections.Generic.EqualityComparer<int>.Default.Equals(value, 0);",
        "bool Run(int value) => EqualityComparer<int>.Default.Equals(value, 0);",
        "using System.Collections.Generic;\n")]
    [InlineData("System.Environment.SpecialFolder Run() => System.Environment.SpecialFolder.Desktop;",
        "Environment.SpecialFolder Run() => Environment.SpecialFolder.Desktop;",
        "using System;\n")]
    [InlineData("System.DayOfWeek DayOfWeek { get; } = System.DayOfWeek.Monday;",
        "DayOfWeek DayOfWeek { get; } = DayOfWeek.Monday;",
        "using System;\n")]
    [InlineData("System.Collections.Generic.List<System.Text.StringBuilder> Run() => [];",
        "List<StringBuilder> Run() => [];",
        "using System.Collections.Generic;\nusing System.Text;\n")]
    public async Task ImportsTypesReachedThroughMemberAccess(string body, string expected, string directives)
    {
        string source = $$"""
            class ExampleType
            {
                {{body}}
            }
            """;

        string target = $$"""
            {{directives}}class ExampleType
            {
                {{expected}}
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixQualifiedTypeImports(source, fixAll: true);

        Assert.Equal(target.ReplaceLineEndings(replacementText: "\n"), fixedSource.ReplaceLineEndings(replacementText: "\n"));
    }

    /// <summary>
    /// Verifies that a reference whose name is already taken is reported for an alias, without a fix.
    /// </summary>
    [Fact]
    public async Task ReportsCollidingReferencesWithoutAFix()
    {
        const string source = """
            using System.Threading;

            namespace Other
            {
                internal sealed class Timer;
            }

            class ExampleType
            {
                Other.Timer Run() => new();
            }
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new QualifiedTypeImportAnalyzer(), source);
        Diagnostic diagnostic = Assert.Single(diagnostics);

        Assert.Equal(QualifiedTypeImportAnalyzer.RuleIdentifier, diagnostic.Id);
        Assert.Contains(expectedSubstring: "under a using alias", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a member access through a namespace is reported for an alias, without a fix, when
    /// its short name would bind to a property or a local instead of the type.
    /// </summary>
    /// <param name="body">Members whose own names take the type's short name.</param>
    [Theory]
    [InlineData("string Directory { get; } = \"\"; string[] Run() => System.IO.Directory.GetFiles(Directory);")]
    [InlineData("int Run(int first) { int Math = first; return System.Math.Abs(Math); }")]
    public async Task ReportsMemberAccessTakenByAnotherName(string body)
    {
        string source = $$"""
            class ExampleType
            {
                {{body}}
            }
            """;

        Diagnostic diagnostic = Assert.Single(await AnalyzerTestHarness.Analyze(new QualifiedTypeImportAnalyzer(), source));

        Assert.Contains(expectedSubstring: "under a using alias", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
