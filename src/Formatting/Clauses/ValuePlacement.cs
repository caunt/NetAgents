using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting.Collections;

namespace NetAgents.Analyzers.Formatting.Clauses;

/// <summary>
/// Finds a value that starts on a line below the <c>=</c> it is assigned with, and joins the two.
/// </summary>
/// <remarks>
/// <para>
/// No formatter decides how deep a line after <c>=</c> goes, and a value left there hides where the statement really
/// breaks:
/// </para>
/// <code>
/// envelope =
///     await JsonSerializer.DeserializeAsync(content, context, cancellationToken)
///         .ConfigureAwait(continueOnCapturedContext: false);
/// </code>
/// <para>
/// The value starts beside its <c>=</c>, and a long one breaks inside itself: at the links of its chain, or at the
/// arguments of its call. Two values keep their own layout: a collection expression NETAGENTS0020 lays out, whose
/// bracket opens below the <c>=</c> when it does not fit, and an initializer written without <c>new</c>, whose brace
/// does. A comment between the <c>=</c> and the value stays where its author wrote it.
/// </para>
/// </remarks>
internal static class ValuePlacement
{
    public const string RuleIdentifier = "NETAGENTS0054";

    // The = of a declaration, a default, or an assignment of any kind, or nothing for any other node.
    public static SyntaxToken GetOperator(SyntaxNode node)
    {
        return node switch
        {
            EqualsValueClauseSyntax clause => clause.EqualsToken,
            AssignmentExpressionSyntax assignment => assignment.OperatorToken,
            _ => default,
        };
    }

    // The value starts on a later line than its =, with nothing but whitespace between the two.
    public static bool IsDetached(SyntaxNode node, SourceText text)
    {
        SyntaxToken assigned = GetOperator(node);
        ExpressionSyntax? value = GetValue(node);

        if (value is null || assigned.IsMissing || value is InitializerExpressionSyntax || CollectionLayout.CanLayOut(value))
            return false;

        // A comment between the = and the value is its author's, who decides which line it belongs to.
        SyntaxToken first = value.GetFirstToken();
        bool plain = IsWhitespace(assigned.TrailingTrivia) && IsWhitespace(first.LeadingTrivia);

        return plain
            && text.Lines.GetLineFromPosition(assigned.Span.End).LineNumber != text.Lines
                .GetLineFromPosition(first.SpanStart)
                .LineNumber;
    }

    // One space after the =, and nothing before the value: total = first + second.
    public static SyntaxNode Join(SyntaxNode root, SyntaxNode node)
    {
        SyntaxToken assigned = GetOperator(node);
        SyntaxToken first = assigned.GetNextToken();

        return root.ReplaceTokens(tokens: [assigned, first], (original, rewritten) => original == assigned
            ? rewritten.WithTrailingTrivia(SyntaxFactory.Space)
            : rewritten.WithLeadingTrivia(SyntaxTriviaList.Empty));
    }

    private static ExpressionSyntax? GetValue(SyntaxNode node)
    {
        return node switch
        {
            EqualsValueClauseSyntax clause => clause.Value,
            AssignmentExpressionSyntax assignment => assignment.Right,
            _ => null,
        };
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
