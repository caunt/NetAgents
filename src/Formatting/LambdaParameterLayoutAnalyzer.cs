using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting;

/// <summary>
/// Requires a lambda with one inferred parameter to omit the optional parentheses.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LambdaParameterLayoutAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = LambdaParameterLayout.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Single lambda parameters omit parentheses",
        messageFormat: "Drop the parentheses around this lambda's single parameter",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.ParenthesizedLambdaExpression);
    }

    private static void AnalyzeLambda(SyntaxNodeAnalysisContext context)
    {
        ParenthesizedLambdaExpressionSyntax lambda = (ParenthesizedLambdaExpressionSyntax)context.Node;

        if (LambdaParameterLayout.IsViolation(lambda))
            context.ReportDiagnostic(Diagnostic.Create(Rule, lambda.ParameterList.GetLocation()));
    }
}
