using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Documentation;

internal static class DocumentationProse
{
    public const string RuleIdentifier = "NETAGENTS0029";

    // The loose text of a comment, or nothing when every word already sits inside an element. The
    // structural newlines and indentation between elements are text nodes too, so only a literal
    // carrying something other than whitespace counts as prose that would be dropped.
    public static XmlTextSyntax? FindLooseProse(DocumentationCommentTriviaSyntax documentation)
    {
        foreach (XmlNodeSyntax node in documentation.Content)
        {
            if (node is not XmlTextSyntax text)
                continue;

            foreach (SyntaxToken token in text.TextTokens)
            {
                if (token.IsKind(SyntaxKind.XmlTextLiteralToken) && !string.IsNullOrWhiteSpace(token.ValueText))
                    return text;
            }
        }

        return null;
    }

    // The written prose, without the blank documentation lines that frame it. Those carry the
    // exterior markers of the surrounding layout and must stay outside the element introduced.
    public static TextSpan GetProseSpan(XmlTextSyntax prose)
    {
        int start = -1;
        int end = -1;

        foreach (SyntaxToken token in prose.TextTokens)
        {
            if (!token.IsKind(SyntaxKind.XmlTextLiteralToken) || string.IsNullOrWhiteSpace(token.ValueText))
                continue;

            if (start < 0)
                start = token.Span.Start;

            end = token.Span.End;
        }

        return start < 0 ? prose.Span : TextSpan.FromBounds(start, end);
    }
}
