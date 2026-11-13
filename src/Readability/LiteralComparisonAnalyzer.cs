using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Requires a comparison with a string literal to be written as a constant pattern rather than a <c>string.Equals</c>
/// call.
/// </summary>
/// <remarks>
/// <c>string.Equals(value, b: "--help", StringComparison.Ordinal)</c> becomes <c>value is "--help"</c>, which compares
/// ordinally in the same way, needs no parameter names, and combines with other literals as
/// <c>value is "-h" or "--help"</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LiteralComparisonAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = LiteralComparison.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Comparisons with a string literal must use a pattern",
        messageFormat: "Compare with the literal through a pattern: write value is \"literal\", or is not \"literal\" for a negated check, which compares ordinally exactly like this string.Equals call; several literals combine as value is \"a\" or \"b\"",
        category: "Readability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeCall, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeCall(SyntaxNodeAnalysisContext context)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;

        if (LiteralComparison.GetPattern(invocation, context.SemanticModel, context.CancellationToken) is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
    }
}
