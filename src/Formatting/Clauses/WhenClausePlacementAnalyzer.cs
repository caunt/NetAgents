using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting.Clauses;

/// <summary>
/// Keeps the <c>when</c> keyword on the line of the clause it guards: after the parentheses of a <c>catch</c>
/// clause, after the pattern of a <c>case</c> label, and after the pattern of a switch expression arm.
/// </summary>
/// <remarks>
/// <para>
/// The guard reads as part of the clause, so the keyword never starts a line of its own:
/// </para>
/// <code>
/// catch (IOException exception) when (exception.HResult == SharingViolation)
/// case int count when count > 0:
/// int count when count > 0 => "positive",
/// </code>
/// <para>
/// Where only whitespace stands between the clause and <c>when</c>, the fix moves <c>when</c> up behind the clause
/// with one space. A comment in between is the author's to move, so the rule then reports without a fix.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WhenClausePlacementAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = WhenClausePlacement.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "The when keyword must stay on the line of the clause it guards",
        messageFormat: "Move 'when' up to the end of the line before it, so it follows {0} on the same line",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeClause, SyntaxKind.CatchFilterClause, SyntaxKind.WhenClause);
    }

    private static void AnalyzeClause(SyntaxNodeAnalysisContext context)
    {
        SyntaxToken keyword = WhenClausePlacement.GetKeyword(context.Node);

        if (WhenClausePlacement.IsMisplaced(keyword))
            context.ReportDiagnostic(Diagnostic.Create(Rule, keyword.GetLocation(), Describe(context.Node)));
    }

    private static string Describe(SyntaxNode clause)
    {
        return clause.Parent switch
        {
            CatchClauseSyntax => "its catch clause",
            CasePatternSwitchLabelSyntax => "its case pattern",
            SwitchExpressionArmSyntax => "its switch arm's pattern",
            _ => "the clause it guards",
        };
    }
}
