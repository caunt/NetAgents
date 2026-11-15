using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting.Clauses;

/// <summary>
/// Requires an assigned value to start on the line of its <c>=</c>.
/// </summary>
/// <remarks>
/// <c>envelope =</c> followed by its value on the next line leaves that line at whatever indentation it was written
/// with, since no formatter places it. The fix joins the value to the <c>=</c>; a long value then breaks inside itself,
/// at the links of its chain or the arguments of its call.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValuePlacementAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = ValuePlacement.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "An assigned value must start on the line of its =",
        messageFormat: "Start this value on the line of its =: no formatter indents a value left on the next line, so write total = first + second and let a long value break inside itself, at the links of its chain or the arguments of its call",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeValue,
            SyntaxKind.EqualsValueClause,
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxKind.AddAssignmentExpression,
            SyntaxKind.SubtractAssignmentExpression,
            SyntaxKind.MultiplyAssignmentExpression,
            SyntaxKind.DivideAssignmentExpression,
            SyntaxKind.ModuloAssignmentExpression,
            SyntaxKind.AndAssignmentExpression,
            SyntaxKind.OrAssignmentExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression,
            SyntaxKind.LeftShiftAssignmentExpression,
            SyntaxKind.RightShiftAssignmentExpression,
            SyntaxKind.UnsignedRightShiftAssignmentExpression,
            SyntaxKind.CoalesceAssignmentExpression);
    }

    private static void AnalyzeValue(SyntaxNodeAnalysisContext context)
    {
        if (ValuePlacement.IsDetached(context.Node, context.Node.SyntaxTree.GetText(context.CancellationToken)))
            context.ReportDiagnostic(Diagnostic.Create(Rule, ValuePlacement.GetOperator(context.Node).GetLocation()));
    }
}
