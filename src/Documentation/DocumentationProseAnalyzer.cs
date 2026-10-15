using System;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Documentation;

/// <summary>
/// Requires documentation prose to sit inside an element so the generated documentation keeps it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocumentationProseAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = DocumentationProse.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Documentation prose sits inside an element",
        messageFormat: "Wrap this description in a summary element; text outside every element reaches no reader",
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

    // A node action registered for structured trivia reports the same comment twice, so the tree
    // is walked once instead, exactly as the sibling documentation rules do.
    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);

        foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: true))
        {
            if (node is DocumentationCommentTriviaSyntax documentation)
                ReportLooseProse(documentation, context);
        }
    }

    // An inherited comment is allowed a trailing note: the element it carries already directs the
    // reader to the base, so nothing the author wrote is being silently discarded.
    private static bool Inherits(DocumentationCommentTriviaSyntax documentation)
    {
        foreach (XmlNodeSyntax node in documentation.Content)
        {
            XmlNameSyntax? name = node switch
            {
                XmlElementSyntax element => element.StartTag.Name,
                XmlEmptyElementSyntax element => element.Name,
                _ => null,
            };

            if (string.Equals(name?.LocalName.ValueText, b: "inheritdoc", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void ReportLooseProse(DocumentationCommentTriviaSyntax documentation, SyntaxTreeAnalysisContext context)
    {
        XmlTextSyntax? prose = Inherits(documentation) ? null : DocumentationProse.FindLooseProse(documentation);

        if (prose is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, prose.GetLocation()));
    }
}
