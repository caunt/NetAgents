using System.Collections.Immutable;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.Analyzers.Reliability;

/// <summary>
/// Suppresses the SDK's populate-switch suggestions for switches a default arm already completes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExhaustiveSwitchSuppressor : DiagnosticSuppressor
{
    private const string Justification =
        "The default arm already handles every remaining member, so naming them adds no coverage, and the fix would shadow the default.";

    private static readonly SuppressionDescriptor ExpressionSuppression = new(id: "NETAGENTS1002", suppressedDiagnosticId: "IDE0072", Justification);

    private static readonly SuppressionDescriptor StatementSuppression = new(id: "NETAGENTS1001", suppressedDiagnosticId: "IDE0010", Justification);

    /// <inheritdoc />
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => [StatementSuppression, ExpressionSuppression];

    /// <inheritdoc />
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (Diagnostic diagnostic in context.ReportedDiagnostics)
        {
            SuppressionDescriptor? descriptor = diagnostic.Id switch
            {
                "IDE0010" => StatementSuppression,
                "IDE0072" => ExpressionSuppression,
                _ => null,
            };

            if (descriptor is null || !CompletesEveryMember(diagnostic, context.CancellationToken))
                continue;

            context.ReportSuppression(Suppression.Create(descriptor, diagnostic));
        }
    }

    // Both rules report on the switch keyword, so the innermost enclosing switch of the reported
    // kind is the one the suggestion describes, even where a switch expression sits inside a switch
    // statement. The catch-all test matches Roslyn's own: a default label, a discard arm, or an
    // unconditional var pattern leaves no member for the suggested cases to cover.
    private static bool CompletesEveryMember(Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        SyntaxTree? tree = diagnostic.Location.SourceTree;

        if (tree is null)
            return false;

        SyntaxNode node = tree.GetRoot(cancellationToken).FindNode(diagnostic.Location.SourceSpan);

        return diagnostic.Id == "IDE0010"
            ? HasDefaultLabel(node.FirstAncestorOrSelf<SwitchStatementSyntax>())
            : HasCatchAllArm(node.FirstAncestorOrSelf<SwitchExpressionSyntax>());
    }

    // Roslyn's syntax lists are structs, so enumerating them directly keeps the search allocation free.
    private static bool HasCatchAllArm(SwitchExpressionSyntax? expression)
    {
        if (expression is null)
            return false;

        foreach (SwitchExpressionArmSyntax arm in expression.Arms)
        {
            if (arm.WhenClause is null && arm.Pattern is DiscardPatternSyntax or VarPatternSyntax)
                return true;
        }

        return false;
    }

    private static bool HasDefaultLabel(SwitchStatementSyntax? statement)
    {
        if (statement is null)
            return false;

        foreach (SwitchSectionSyntax section in statement.Sections)
        {
            foreach (SwitchLabelSyntax label in section.Labels)
            {
                if (label is DefaultSwitchLabelSyntax)
                    return true;
            }
        }

        return false;
    }
}
