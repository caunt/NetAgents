using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting.Chains;

/// <summary>
/// Requires member chains to take one layout: on one line when they hold up to four links and fit, otherwise one call
/// per line with property and field accesses kept on the line of what they follow.
/// </summary>
/// <remarks>
/// <code>
/// await Console
///     .Error.WriteLineAsync($"Error: {exception.Message}")
///     .ConfigureAwait(continueOnCapturedContext: false);
/// </code>
/// <para>
/// fits within 128 characters with three links, so it becomes one line. A chain of more than four links, or one that
/// does not fit, starts every call on its own line one level deeper, while <c>Console.Error</c> or
/// <c>rows.Values</c> stays whole on the line before. The fix only moves line breaks before dots; the formatter
/// indents the lines.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChainLayoutAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = ChainLayout.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Member chains must use the standard layout",
        messageFormat: "Lay this chain out the standard way: up to 4 member accesses and calls on one line when it fits within 128 characters; otherwise each call on its own line, one level deeper, with property and field accesses kept on the line of what they follow",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeChain, SyntaxKind.InvocationExpression, SyntaxKind.SimpleMemberAccessExpression, SyntaxKind.ElementAccessExpression);
    }

    private static void AnalyzeChain(SyntaxNodeAnalysisContext context)
    {
        ExpressionSyntax top = (ExpressionSyntax)context.Node;

        if (!ChainLayout.IsTop(top))
            return;

        List<ChainLink>? links = ChainLayout.GetLinks(top, context.SemanticModel, context.CancellationToken);

        if (links is null)
            return;

        SourceText text = top.SyntaxTree.GetText(context.CancellationToken);
        bool? split = ChainLayout.GetRequiredSplit(top, links, text);

        if (split is bool required && !ChainLayout.IsSettled(links, required, text))
            context.ReportDiagnostic(Diagnostic.Create(Rule, top.GetLocation()));
    }
}
