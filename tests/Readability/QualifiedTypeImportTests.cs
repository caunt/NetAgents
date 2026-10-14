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
    public async Task AllowsReferencesThatCannotBeImported(string body)
    {
        string source = $$"""
            class Outer
            {
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

        Assert.Equal(
            expected: """
            using System;
            using System.Text;

            class ExampleType
            {
                StringBuilder Run() => new();
            }
            """,
            fixedSource
        );
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
}
