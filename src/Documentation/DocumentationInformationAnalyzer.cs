using System;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Documentation;

/// <summary>
/// Requires a documentation element to state something its subject's name does not already.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocumentationInformationAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = DocumentationInformation.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Documentation comments add to the name they describe",
        messageFormat: "Describe what '{0}' is for; repeating its name leaves the reader where they started",
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
    // is walked once instead, exactly as the sibling summary rule does.
    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);

        foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: true))
        {
            if (node is DocumentationCommentTriviaSyntax documentation)
                ReportEmptyElements(documentation, context);
        }
    }

    private static string? GetAttribute(XmlNodeSyntax node)
    {
        SyntaxList<XmlAttributeSyntax> attributes = node switch
        {
            XmlElementSyntax element => element.StartTag.Attributes,
            XmlEmptyElementSyntax element => element.Attributes,
            _ => default,
        };

        foreach (XmlAttributeSyntax attribute in attributes)
        {
            if (attribute is XmlNameAttributeSyntax named)
                return named.Identifier.Identifier.ValueText;
        }

        return null;
    }

    private static string? GetName(XmlNodeSyntax node)
    {
        XmlNameSyntax? name = node switch
        {
            XmlElementSyntax element => element.StartTag.Name,
            XmlEmptyElementSyntax element => element.Name,
            _ => null,
        };

        return name?.LocalName.ValueText;
    }

    // The declaration a summary belongs to, skipping the attribute lists and modifiers the comment
    // sits above. An indexer and the operators have no name worth comparing, so they report nothing.
    private static string? GetSubject(DocumentationCommentTriviaSyntax documentation)
    {
        for (SyntaxNode? node = documentation.ParentTrivia.Token.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case BaseTypeDeclarationSyntax declaration:
                    return declaration.Identifier.ValueText;
                case DelegateDeclarationSyntax declaration:
                    return declaration.Identifier.ValueText;
                case EnumMemberDeclarationSyntax declaration:
                    return declaration.Identifier.ValueText;
                case MethodDeclarationSyntax declaration:
                    return declaration.Identifier.ValueText;
                case PropertyDeclarationSyntax declaration:
                    return declaration.Identifier.ValueText;
                case EventDeclarationSyntax declaration:
                    return declaration.Identifier.ValueText;
                case ConstructorDeclarationSyntax declaration:
                    return declaration.Identifier.ValueText;
                case BaseFieldDeclarationSyntax declaration:
                    return declaration.Declaration.Variables.Count == 0 ? null : declaration.Declaration.Variables[index: 0].Identifier.ValueText;
                case MemberDeclarationSyntax:
                    return null;
                default:
                    continue;
            }
        }

        return null;
    }

    // An inherited comment states nothing of its own on purpose, and the base it points at carries
    // whatever there is to judge.
    private static bool Inherits(DocumentationCommentTriviaSyntax documentation)
    {
        foreach (XmlNodeSyntax node in documentation.Content)
        {
            if (string.Equals(GetName(node), b: "inheritdoc", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void ReportEmptyElements(DocumentationCommentTriviaSyntax documentation, SyntaxTreeAnalysisContext context)
    {
        if (Inherits(documentation))
            return;

        foreach (XmlNodeSyntax node in documentation.Content)
        {
            string? element = GetName(node);

            bool describes = string.Equals(element, b: "summary", StringComparison.Ordinal)
                || string.Equals(element, b: "param", StringComparison.Ordinal)
                || string.Equals(element, b: "typeparam", StringComparison.Ordinal);

            if (!describes)
                continue;

            string? subject = string.Equals(element, b: "summary", StringComparison.Ordinal)
                ? GetSubject(documentation)
                : GetAttribute(node);

            if (subject is null || !DocumentationInformation.RestatesName(node.ToString(), subject))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), subject));
        }
    }
}
