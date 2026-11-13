using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting.Members;

/// <summary>
/// Finds members wrapped at the <c>=</c> of an initializer or the <c>=&gt;</c> of an expression body although the
/// whole member fits on one line, and joins them.
/// </summary>
internal static class WrappedMember
{
    public const string RuleIdentifier = "NETAGENTS0042";

    public static SyntaxToken GetOperator(MemberDeclarationSyntax member)
    {
        return member switch
        {
            BaseFieldDeclarationSyntax field => GetFieldOperator(field),
            PropertyDeclarationSyntax { ExpressionBody: not null } property => property.ExpressionBody.ArrowToken,
            PropertyDeclarationSyntax { Initializer: not null } property => property.Initializer.EqualsToken,
            BaseMethodDeclarationSyntax { ExpressionBody: not null } method => method.ExpressionBody.ArrowToken,
            IndexerDeclarationSyntax { ExpressionBody: not null } indexer => indexer.ExpressionBody.ArrowToken,
            _ => default,
        };
    }

    // Only the break around the operator is joined: a member that spans lines anywhere else is laid out by the rules
    // for argument lists, initializers, and lambdas, and a comment beside the operator stays where its author put it.
    public static bool IsWrapped(MemberDeclarationSyntax member, SyntaxToken operatorToken, SourceText text)
    {
        SyntaxToken before = operatorToken.GetPreviousToken();
        SyntaxToken after = operatorToken.GetNextToken();
        bool plain = IsWhitespace(before.TrailingTrivia, operatorToken.LeadingTrivia, operatorToken.TrailingTrivia, after.LeadingTrivia);

        if (operatorToken.IsKind(SyntaxKind.None) || !plain)
            return false;

        bool brokenBefore = GetLine(text, before.Span.End) != GetLine(text, operatorToken.SpanStart);

        if (!brokenBefore && GetLine(text, operatorToken.Span.End) == GetLine(text, after.SpanStart))
            return false;

        SyntaxToken first = member.AttributeLists.Count > 0 ? member.AttributeLists[member.AttributeLists.Count - 1].GetLastToken().GetNextToken() : member.GetFirstToken();
        string head = text.ToString(TextSpan.FromBounds(first.SpanStart, before.Span.End));
        string tail = text.ToString(TextSpan.FromBounds(after.SpanStart, member.Span.End));

        if (head.Contains(value: "\n") || tail.Contains(value: "\n"))
            return false;

        int indentation = first.SpanStart - text.Lines.GetLineFromPosition(first.SpanStart).Start;

        return indentation + head.Length + operatorToken.Text.Length + tail.Length + 2 <= LayoutLimits.MaximumInlineLength;
    }

    public static SyntaxNode Join(SyntaxNode root, SyntaxToken operatorToken)
    {
        SyntaxToken before = operatorToken.GetPreviousToken();
        SyntaxToken after = operatorToken.GetNextToken();

        return root.ReplaceTokens(tokens: [before, operatorToken, after], (original, rewritten) => Respace(original, rewritten, before, after));
    }

    private static SyntaxToken GetFieldOperator(BaseFieldDeclarationSyntax field)
    {
        SeparatedSyntaxList<VariableDeclaratorSyntax> variables = field.Declaration.Variables;

        return variables.Count == 1 && variables[index: 0].Initializer is EqualsValueClauseSyntax initializer ? initializer.EqualsToken : default;
    }

    private static int GetLine(SourceText text, int position)
    {
        return text.Lines.GetLineFromPosition(position).LineNumber;
    }

    private static bool IsWhitespace(params SyntaxTriviaList[] lists)
    {
        foreach (SyntaxTriviaList list in lists)
        {
            foreach (SyntaxTrivia trivia in list)
            {
                if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                    return false;
            }
        }

        return true;
    }

    // One space on each side of the operator, and nothing else between the three tokens.
    private static SyntaxToken Respace(SyntaxToken original, SyntaxToken rewritten, SyntaxToken before, SyntaxToken after)
    {
        return original == before
            ? rewritten.WithTrailingTrivia(SyntaxFactory.Space)
            : original == after ? rewritten.WithLeadingTrivia(SyntaxTriviaList.Empty) : rewritten.WithLeadingTrivia(SyntaxTriviaList.Empty).WithTrailingTrivia(SyntaxFactory.Space);
    }
}
