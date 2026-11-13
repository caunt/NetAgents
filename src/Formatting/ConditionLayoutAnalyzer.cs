using System.Globalization;

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

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Conditions must fit on one short line",
        messageFormat: "Keep the condition on one line and within {0} characters: move the long part into a local named for what it means, such as bool hasCapacity = …, and test that." + PolicyMessages.ChangeTheCode,
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeCondition,
            SyntaxKind.IfStatement,
            SyntaxKind.WhileStatement,
            SyntaxKind.DoStatement,
            SyntaxKind.ForStatement,
            SyntaxKind.ConditionalExpression,
            SyntaxKind.CatchFilterClause,
            SyntaxKind.WhenClause);
    }

    private static void AnalyzeCondition(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.ContainsDiagnostics)
            return;

        ExpressionSyntax testedExpression;
        TextSpan span;

        switch (context.Node)
        {
            case IfStatementSyntax statement:
                {
                    testedExpression = statement.Condition;
                    span = TextSpan.FromBounds(statement.OpenParenToken.Span.End, statement.CloseParenToken.SpanStart);

                    break;
                }
            case WhileStatementSyntax statement:
                {
                    testedExpression = statement.Condition;
                    span = TextSpan.FromBounds(statement.OpenParenToken.Span.End, statement.CloseParenToken.SpanStart);

                    break;
                }
            case DoStatementSyntax statement:
                {
                    testedExpression = statement.Condition;
                    span = TextSpan.FromBounds(statement.OpenParenToken.Span.End, statement.CloseParenToken.SpanStart);

                    break;
                }
            case ForStatementSyntax statement when statement.Condition is not null:
                {
                    testedExpression = statement.Condition;
                    span = TextSpan.FromBounds(statement.FirstSemicolonToken.Span.End, statement.SecondSemicolonToken.SpanStart);

                    break;
                }
            case CatchFilterClauseSyntax filter:
                {
                    testedExpression = filter.FilterExpression;
                    span = TextSpan.FromBounds(filter.OpenParenToken.Span.End, filter.CloseParenToken.SpanStart);

                    break;
                }
            case ConditionalExpressionSyntax expression:
                {
                    testedExpression = expression.Condition;
                    span = testedExpression.Span;

                    break;
                }
            case WhenClauseSyntax clause:
                {
                    testedExpression = clause.Condition;
                    span = TextSpan.FromBounds(clause.WhenKeyword.Span.End, testedExpression.Span.End);

                    break;
                }
            default:
                return;
        }

        SourceText source = context.Node.SyntaxTree.GetText(context.CancellationToken);

        bool isMultiline = source.Lines.GetLineFromPosition(span.Start).LineNumber
            != source.Lines.GetLineFromPosition(span.End).LineNumber;

        if (isMultiline || span.Length > LayoutLimits.MaximumInlineLength)
            context.ReportDiagnostic(Diagnostic.Create(Rule, testedExpression.GetLocation(), LayoutLimits.MaximumInlineLength.ToString(CultureInfo.InvariantCulture)));
    }
}
