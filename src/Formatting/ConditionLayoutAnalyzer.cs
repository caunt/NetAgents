using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting;

/// <summary>
/// Requires single-line conditions containing at most 128 characters.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConditionLayoutAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0019";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Conditions must fit on one short line",
        messageFormat: "Keep the condition on one line and within 128 characters; move longer conditions into a separate variable declaration",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeCondition,
            SyntaxKind.IfStatement, SyntaxKind.WhileStatement, SyntaxKind.DoStatement,
            SyntaxKind.ForStatement, SyntaxKind.ConditionalExpression,
            SyntaxKind.CatchFilterClause, SyntaxKind.WhenClause);
    }

    private static void AnalyzeCondition(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.ContainsDiagnostics)
            return;

        ExpressionSyntax condition;
        TextSpan span;

        switch (context.Node)
        {
            case IfStatementSyntax statement:
                {
                    condition = statement.Condition;
                    span = TextSpan.FromBounds(statement.OpenParenToken.Span.End, statement.CloseParenToken.SpanStart);

                    break;
                }
            case WhileStatementSyntax statement:
                {
                    condition = statement.Condition;
                    span = TextSpan.FromBounds(statement.OpenParenToken.Span.End, statement.CloseParenToken.SpanStart);

                    break;
                }
            case DoStatementSyntax statement:
                {
                    condition = statement.Condition;
                    span = TextSpan.FromBounds(statement.OpenParenToken.Span.End, statement.CloseParenToken.SpanStart);

                    break;
                }
            case ForStatementSyntax statement when statement.Condition is not null:
                {
                    condition = statement.Condition;
                    span = TextSpan.FromBounds(statement.FirstSemicolonToken.Span.End, statement.SecondSemicolonToken.SpanStart);

                    break;
                }
            case CatchFilterClauseSyntax filter:
                {
                    condition = filter.FilterExpression;
                    span = TextSpan.FromBounds(filter.OpenParenToken.Span.End, filter.CloseParenToken.SpanStart);

                    break;
                }
            case ConditionalExpressionSyntax expression:
                {
                    condition = expression.Condition;
                    span = condition.Span;

                    break;
                }
            case WhenClauseSyntax clause:
                {
                    condition = clause.Condition;
                    span = TextSpan.FromBounds(clause.WhenKeyword.Span.End, condition.Span.End);

                    break;
                }
            default:
                return;
        }

        SourceText source = context.Node.SyntaxTree.GetText(context.CancellationToken);

        bool isMultiline = source.Lines.GetLineFromPosition(span.Start).LineNumber
            != source.Lines.GetLineFromPosition(span.End).LineNumber;

        if (isMultiline || span.Length > 128)
            context.ReportDiagnostic(Diagnostic.Create(Rule, condition.GetLocation()));
    }
}
