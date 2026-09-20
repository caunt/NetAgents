using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting;

internal static class BlockPadding
{
    public const string RuleIdentifier = "NETAGENTS0023";

    public static ImmutableDictionary<TextSpan, TextChange> GetChanges(SyntaxNode root)
    {
        ImmutableDictionary<TextSpan, TextChange>.Builder changes = ImmutableDictionary.CreateBuilder<TextSpan, TextChange>();
        List<TextSpan> removals = [];

        foreach (SyntaxToken token in root.DescendantTokens())
        {
            TextSpan? padding = token.IsKind(SyntaxKind.OpenBraceToken)
                ? GetPaddingAfter(token)
                : token.IsKind(SyntaxKind.CloseBraceToken) ? GetPaddingBefore(token) : null;

            // An empty block reaches the same blank lines from both edges.
            if (padding is not TextSpan span || removals.Any(existing => existing.OverlapsWith(span)))
                continue;

            removals.Add(span);
            changes.Add(token.Span, new TextChange(span, newText: string.Empty));
        }

        return changes.ToImmutable();
    }

    private static TextSpan? GetPaddingAfter(SyntaxToken brace)
    {
        List<SyntaxTrivia> trivia = [.. brace.TrailingTrivia, .. brace.GetNextToken().LeadingTrivia];
        List<SyntaxTrivia> endings = [];

        foreach (SyntaxTrivia candidate in trivia)
        {
            if (candidate.IsKind(SyntaxKind.WhitespaceTrivia))
                continue;

            if (!candidate.IsKind(SyntaxKind.EndOfLineTrivia))
                return candidate.IsDirective ? null : GetRemoval(endings);

            endings.Add(candidate);
        }

        return GetRemoval(endings);
    }

    private static TextSpan? GetPaddingBefore(SyntaxToken brace)
    {
        List<SyntaxTrivia> trivia = [.. brace.GetPreviousToken().TrailingTrivia, .. brace.LeadingTrivia];
        List<SyntaxTrivia> endings = [];

        for (int index = trivia.Count - 1; index >= 0; index--)
        {
            SyntaxTrivia candidate = trivia[index];

            if (candidate.IsKind(SyntaxKind.WhitespaceTrivia))
                continue;

            if (!candidate.IsKind(SyntaxKind.EndOfLineTrivia))
                return candidate.IsDirective ? null : GetRemoval(endings);

            endings.Insert(index: 0, candidate);
        }

        return GetRemoval(endings);
    }

    private static TextSpan? GetRemoval(List<SyntaxTrivia> endings)
    {
        // The first line ending closes the brace line; every later one is padding.
        return endings.Count < 2 ? null : TextSpan.FromBounds(endings[index: 0].Span.End, endings[index: endings.Count - 1].Span.End);
    }
}
