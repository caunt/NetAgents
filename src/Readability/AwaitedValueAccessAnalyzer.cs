using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Rejects reading a member of a parenthesized <c>await</c>, so the awaited value gets a name of its own.
/// </summary>
/// <remarks>
/// <code>
/// if ((await client.GetAppAsync(id)).Versions.Count is 0)   // reported
///
/// IpaApp app = await client.GetAppAsync(id);
///
/// if (app.Versions.Count is 0)                              // accepted
/// </code>
/// <para>
/// Member access, element access, and conditional access on the parentheses are all reported. There is no
/// automatic fix, since only the author knows what the awaited value is and what to call it.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AwaitedValueAccessAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0044";

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Awaited values must be named before their members are read",
        messageFormat: "Store the result of this await in a local named for what it is, then use its members, as in IpaApp app = await client.GetAppAsync(id); followed by if (app.Versions.Count is 0). Reaching into a parenthesized await hides what the value is and stretches the expression over lines." + PolicyMessages.ChangeTheCode,
        category: "Readability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeParentheses, SyntaxKind.ParenthesizedExpression);
    }

    private static void AnalyzeParentheses(SyntaxNodeAnalysisContext context)
    {
        ParenthesizedExpressionSyntax parentheses = (ParenthesizedExpressionSyntax)context.Node;

        if (parentheses.Expression is not AwaitExpressionSyntax)
            return;

        bool accessed = parentheses.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Expression == parentheses,
            ElementAccessExpressionSyntax element => element.Expression == parentheses,
            ConditionalAccessExpressionSyntax conditional => conditional.Expression == parentheses,
            _ => false,
        };

        if (accessed)
            context.ReportDiagnostic(Diagnostic.Create(Rule, parentheses.GetLocation()));
    }
}
