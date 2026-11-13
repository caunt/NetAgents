using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting.Chains;

namespace NetAgents.Analyzers.Formatting.Statements;

/// <summary>
/// Finds simple statements spread over several lines although they fit on one, and joins them.
/// </summary>
/// <remarks>
/// Declarations, expression statements, <c>return</c>, <c>throw</c>, and <c>yield</c> are covered. A statement is
/// left as written when another rule owns its layout or joining would lose something: a block inside it, as in a
/// lambda with statements, a switch expression, a comment or directive between its tokens, or a token that itself
/// spans lines, such as a verbatim string.
/// </remarks>
internal static class OneLineStatement
{
    public const string RuleIdentifier = "NETAGENTS0045";

    public static bool IsJoinable(StatementSyntax statement, SemanticModel model, SourceText text, CancellationToken cancellationToken)
    {
        bool simple = statement is LocalDeclarationStatementSyntax or ExpressionStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax;

        if (!simple && statement is not YieldStatementSyntax)
            return false;

        TextLine first = text.Lines.GetLineFromPosition(statement.SpanStart);

        if (first.LineNumber == text.Lines.GetLineFromPosition(statement.Span.End).LineNumber || HasOwnLayout(statement))
            return false;

        // A chain of more than four links splits even when the statement would fit, and NETAGENTS0047 owns its lines.
        if (ChainLayout.RequiresSplit(statement, model, text, cancellationToken))
            return false;

        // Every line break becomes one space, which the formatter may only shorten, so this bounds the joined width.
        int width = statement.SpanStart - first.Start;
        SyntaxToken last = statement.GetLastToken();

        foreach (SyntaxToken token in statement.DescendantTokens())
        {
            width += token.Text.Length;

            if (token != last && (token.HasTrailingTrivia || token.GetNextToken().HasLeadingTrivia))
                width++;
        }

        return width <= LayoutLimits.MaximumInlineLength;
    }

    // Each gap between the statement's tokens becomes one space where it held whitespace, and nothing where it held
    // none; the formatter then settles the spaces the way it would on any line.
    public static StatementSyntax Join(StatementSyntax statement)
    {
        SyntaxToken first = statement.GetFirstToken();
        SyntaxToken last = statement.GetLastToken();

        return statement.ReplaceTokens(statement.DescendantTokens(),
            (original, rewritten) =>
            {
                SyntaxToken joined = original == first ? rewritten : rewritten.WithLeadingTrivia(SyntaxTriviaList.Empty);

                return original == last
                    ? joined
                    : original.HasTrailingTrivia || original.GetNextToken().HasLeadingTrivia ? joined.WithTrailingTrivia(SyntaxFactory.Space) : joined.WithTrailingTrivia(SyntaxTriviaList.Empty);
            });
    }

    private static bool HasOwnLayout(StatementSyntax statement)
    {
        foreach (SyntaxNode node in statement.DescendantNodes())
        {
            if (node is BlockSyntax or SwitchExpressionSyntax)
                return true;
        }

        SyntaxToken first = statement.GetFirstToken();
        SyntaxToken last = statement.GetLastToken();

        foreach (SyntaxToken token in statement.DescendantTokens())
        {
            bool inner = token != first && !IsWhitespace(token.LeadingTrivia);

            if (token.Text.Contains(value: "\n") || inner || (token != last && !IsWhitespace(token.TrailingTrivia)))
                return true;
        }

        return false;
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
