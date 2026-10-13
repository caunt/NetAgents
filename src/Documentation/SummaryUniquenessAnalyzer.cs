using System;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Documentation;

/// <summary>
/// Requires every documentation comment to describe its declaration exactly once.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SummaryUniquenessAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0025";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Documentation comments describe a declaration once",
        messageFormat: "Keep one summary and describe the rest with param, returns, or remarks elements",
        category: "Documentation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    // Walking the tree once visits each documentation node once; a node action registered for
    // structured trivia reports the same comment twice. Consecutive comment lines parse into one
    // node, so stacked blocks arrive together and each summary is reported where it is written.
    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);

        foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: true))
        {
            if (node is DocumentationCommentTriviaSyntax documentation)
                ReportRepeatedSummaries(documentation, context);
        }
    }

    private static bool IsSummary(XmlNodeSyntax node)
    {
        XmlNameSyntax? name = node switch
        {
            XmlElementSyntax element => element.StartTag.Name,
            XmlEmptyElementSyntax element => element.Name,
            _ => null,
        };

        return name is not null && string.Equals(name.LocalName.ValueText, b: "summary", StringComparison.Ordinal);
    }

    private static void ReportRepeatedSummaries(DocumentationCommentTriviaSyntax documentation, SyntaxTreeAnalysisContext context)
    {
        bool described = false;

        foreach (XmlNodeSyntax node in documentation.Content)
        {
            if (!IsSummary(node))
                continue;

            if (described)
                context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));

            described = true;
        }
    }
}
