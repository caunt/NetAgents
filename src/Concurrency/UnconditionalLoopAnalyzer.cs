using System;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.Analyzers.Concurrency;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnconditionalLoopAnalyzer : DiagnosticAnalyzer
{
    public const string RuleIdentifier = "NETAGENTS0008";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Unconditional loops are forbidden",
        messageFormat: "Use an explicit termination or cancellation condition",
        category: "Concurrency",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.WhileStatement, SyntaxKind.DoStatement, SyntaxKind.ForStatement);
    }

    private static void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
    {
        ExpressionSyntax? condition = context.Node switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Condition,
            DoStatementSyntax doStatement => doStatement.Condition,
            ForStatementSyntax forStatement => forStatement.Condition,
            _ => null,
        };
        while (condition is ParenthesizedExpressionSyntax parenthesizedExpression)
        {
            condition = parenthesizedExpression.Expression;
        }

        if (condition is not null && !condition.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }
}