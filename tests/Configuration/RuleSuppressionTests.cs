using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Configuration;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Configuration;

/// <summary>
/// Covers suppressions of the rules the build requires, written as attributes or pragmas.
/// </summary>
public sealed class RuleSuppressionTests
{
    private const string Type = "using System.Diagnostics.CodeAnalysis;\n\nCODE\npublic static class ExampleType\n{\n    MEMBER\n    public static int Run() => 1;\n}\n";

    /// <summary>
    /// Verifies suppressions of other analyzers stay available, as do a restore on its own and an attribute that only
    /// shares the name, while a pragma restoring a rule is no suppression at all.
    /// </summary>
    /// <param name="code">What precedes the type, such as a pragma or an assembly attribute.</param>
    /// <param name="member">An attribute placed on the method.</param>
    [Theory]
    [InlineData("", "[SuppressMessage(\"MicrosoftCodeAnalysisCorrectness\", \"RS1001\")]")]
    [InlineData("#pragma warning disable RS1001", "")]
    [InlineData("#pragma warning restore CA2000", "")]
    [InlineData("[System.AttributeUsage(System.AttributeTargets.All)]\npublic sealed class SuppressMessageAttribute(string category, string checkId) : System.Attribute\n{\n"
        + "    public string Category { get; } = category;\n\n    public string CheckId { get; } = checkId;\n}\n",
        "[SuppressMessage(\"Design\", \"CA1031\")]")]
    public async Task AllowsOtherSuppressions(string code, string member)
    {
        Assert.Empty(await Analyze(code, member));
    }

    /// <summary>
    /// Verifies a suppression of a NetAgents, code analysis, style, or documentation rule is reported, whether it is an
    /// attribute on a member or the assembly, names the rule positionally or by name, carries a title after the
    /// identifier, names it through a constant, or is a pragma, including one that silences every warning.
    /// </summary>
    /// <param name="code">What precedes the type.</param>
    /// <param name="member">An attribute placed on the method.</param>
    [Theory]
    [InlineData("", "[SuppressMessage(\"Design\", \"CA1031\")]")]
    [InlineData("", "[SuppressMessage(\"Style\", \"IDE0055:Fix formatting\")]")]
    [InlineData("", "[SuppressMessage(checkId: \"NETAGENTS0024\", category: \"Design\")]")]
    [InlineData("", "[UnconditionalSuppressMessage(\"Performance\", \"CA1819\")]")]
    [InlineData("[assembly: SuppressMessage(\"Documentation\", \"CS1591\")]", "")]
    [InlineData("#pragma warning disable CA2000", "")]
    [InlineData("#pragma warning disable 1591", "")]
    [InlineData("#pragma warning disable", "")]
    [InlineData("internal static class Rules\n{\n    public const string Catch = \"CA1031\";\n}\n", "[SuppressMessage(\"Design\", Rules.Catch)]")]
    public async Task ReportsRequiredRuleSuppressions(string code, string member)
    {
        Diagnostic diagnostic = Assert.Single(await Analyze(code, member));

        Assert.Equal(RuleSuppressionAnalyzer.RuleIdentifier, diagnostic.Id);
    }

    private static async Task<Diagnostic[]> Analyze(string code, string member)
    {
        string source = Type
            .Replace(oldValue: "CODE", code, StringComparison.Ordinal)
            .Replace(oldValue: "MEMBER", member, StringComparison.Ordinal);

        return await AnalyzerTestHarness
            .Analyze(new RuleSuppressionAnalyzer(), source)
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
