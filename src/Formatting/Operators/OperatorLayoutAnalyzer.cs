using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting.Operators;

/// <summary>
/// Requires <c>&amp;&amp;</c>, <c>||</c>, and <c>??</c> sequences and <c>?:</c> expressions to take one layout: on one
/// line when they fit within 128 characters, otherwise one operand per line with the operator in front.
/// </summary>
/// <remarks>
/// A long line breaks at these operators before it breaks at a call's arguments, so
/// <c>first(…) &amp;&amp; second(…) &amp;&amp; third(a, b, c)</c> becomes three lines that start with their operand or
/// <c>&amp;&amp;</c>, instead of one line whose last call wraps its arguments. The fix only moves line breaks.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OperatorLayoutAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = OperatorLayout.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Logical, coalescing, and conditional operators must use the standard layout",
        messageFormat: "Lay this expression out the standard way: on one line when it fits within 128 characters; otherwise every operand after the first on its own line, one level deeper, starting with its &&, ||, ??, ?, or :",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeExpression,
            SyntaxKind.LogicalAndExpression,
            SyntaxKind.LogicalOrExpression,
            SyntaxKind.CoalesceExpression,
            SyntaxKind.ConditionalExpression);
    }

    private static void AnalyzeExpression(SyntaxNodeAnalysisContext context)
    {
        ExpressionSyntax node = (ExpressionSyntax)context.Node;
        List<SyntaxToken>? operators = OperatorLayout.GetOperators(node);

        if (operators is null)
            return;

        SourceText text = node.SyntaxTree.GetText(context.CancellationToken);
        bool? split = OperatorLayout.GetRequiredSplit(node, operators, context.SemanticModel, text, context.CancellationToken);

        if (split is bool required && !OperatorLayout.IsSettled(node, operators, required, text))
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
    }
}
