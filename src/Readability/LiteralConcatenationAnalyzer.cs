using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Requires string literals and interpolated strings joined with <c>+</c> on one line to be written as one string.
/// </summary>
/// <remarks>
/// <c>$"{description}{Environment.NewLine}" + $"Usage: {usage}"</c> is one piece of text split in two; the fix writes
/// <c>$"{description}{Environment.NewLine}Usage: {usage}"</c>. Pieces split over lines are left as their author laid
/// them out.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LiteralConcatenationAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = LiteralConcatenation.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Strings joined with + on one line must be written as one string",
        messageFormat: "Write these strings as one: they sit on one line, so the + only splits one piece of text in two",
        category: "Readability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeAddition, SyntaxKind.AddExpression);
    }

    private static void AnalyzeAddition(SyntaxNodeAnalysisContext context)
    {
        BinaryExpressionSyntax addition = (BinaryExpressionSyntax)context.Node;
        SourceText text = addition.SyntaxTree.GetText(context.CancellationToken);

        if (LiteralConcatenation.GetMerged(addition, context.SemanticModel, text, context.CancellationToken) is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, addition.GetLocation()));
    }
}
