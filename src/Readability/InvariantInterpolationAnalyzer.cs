using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Requires an interpolated string whose holes print the same under every culture to be written without
/// <c>string.Create(CultureInfo.InvariantCulture, …)</c>.
/// </summary>
/// <remarks>
/// The culture changes how numbers and dates print, so around a string whose holes hold only strings, <c>bool</c>,
/// <c>char</c>, enumeration, or <c>Guid</c> values the call adds nothing; the fix writes the string alone.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InvariantInterpolationAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = InvariantInterpolation.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Interpolated strings that print the same under every culture must be written directly",
        messageFormat: "Write this interpolated string without string.Create: none of its holes prints differently under another culture",
        category: "Readability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        InvocationExpressionSyntax call = (InvocationExpressionSyntax)context.Node;

        if (InvariantInterpolation.GetUnwrapped(call, context.SemanticModel, context.CancellationToken) is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, call.GetLocation()));
    }
}
