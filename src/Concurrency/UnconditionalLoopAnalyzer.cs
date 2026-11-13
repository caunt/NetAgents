using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Concurrency;

/// <summary>
/// Rejects loops without a terminating condition.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnconditionalLoopAnalyzer() : PolicyAnalyzer(Rule, analyzesRazorCode: true)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0008";

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Unconditional loops are forbidden",
        messageFormat: "Give the loop a condition its body or a cancellation token actually changes, so a reader sees when it stops; while (true) with a break inside, or a flag nothing sets, still never says." + PolicyMessages.ChangeTheCode,
        category: "Concurrency",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.WhileStatement, SyntaxKind.DoStatement, SyntaxKind.ForStatement);
    }

    private static void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
    {
        ExpressionSyntax? loopCondition = context.Node switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Condition,
            DoStatementSyntax doStatement => doStatement.Condition,
            ForStatementSyntax forStatement => forStatement.Condition,
            _ => null,
        };

        while (loopCondition is ParenthesizedExpressionSyntax parenthesizedExpression)
            loopCondition = parenthesizedExpression.Expression;

        if (loopCondition is not null && !loopCondition.IsKind(SyntaxKind.TrueLiteralExpression))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }
}
