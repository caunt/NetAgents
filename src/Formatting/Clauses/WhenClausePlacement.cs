using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Formatting.Clauses;

/// <summary>
/// Decides where the <c>when</c> keyword of a catch filter, a case guard, or a switch arm guard belongs, and moves
/// it there.
/// </summary>
internal static class WhenClausePlacement
{
    public const string RuleIdentifier = "NETAGENTS0041";

    // Only whitespace may be dropped between the clause and the keyword; a comment there is the author's to move.
    public static bool CanMove(SyntaxToken keyword)
    {
        SyntaxToken previous = keyword.GetPreviousToken();

        return keyword.IsKind(SyntaxKind.WhenKeyword) && IsWhitespace(previous.TrailingTrivia) && IsWhitespace(keyword.LeadingTrivia);
    }

    public static SyntaxToken GetKeyword(SyntaxNode clause)
    {
        return clause switch
        {
            CatchFilterClauseSyntax filter => filter.WhenKeyword,
            WhenClauseSyntax guard => guard.WhenKeyword,
            _ => default,
        };
    }

    public static bool IsMisplaced(SyntaxToken keyword)
    {
        SyntaxToken previous = keyword.GetPreviousToken();

        if (keyword.IsMissing || previous.IsKind(SyntaxKind.None))
            return false;

        int previousLine = previous.GetLocation().GetLineSpan().EndLinePosition.Line;

        return previousLine != keyword.GetLocation().GetLineSpan().StartLinePosition.Line;
    }

    // One space after the clause, and nothing before the keyword: catch (IOException exception) when (…).
    public static SyntaxNode MoveUp(SyntaxNode root, SyntaxToken keyword)
    {
        SyntaxToken previous = keyword.GetPreviousToken();

        return root.ReplaceTokens(tokens: [previous, keyword],
            (original, rewritten) => original == previous ? rewritten.WithTrailingTrivia(SyntaxFactory.Space) : rewritten.WithLeadingTrivia(SyntaxTriviaList.Empty));
    }

    private static bool IsWhitespace(SyntaxTriviaList trivia)
    {
        foreach (SyntaxTrivia piece in trivia)
        {
            if (!piece.IsKind(SyntaxKind.WhitespaceTrivia) && !piece.IsKind(SyntaxKind.EndOfLineTrivia))
                return false;
        }

        return true;
    }
}
