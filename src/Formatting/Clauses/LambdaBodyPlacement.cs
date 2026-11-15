using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting.Clauses;

/// <summary>
/// Finds an expression-bodied lambda whose body starts on a line below its <c>=&gt;</c>, and joins the two.
/// </summary>
/// <remarks>
/// <para>
/// No formatter decides how deep a line after <c>=&gt;</c> goes, so a body left there keeps whatever indentation it
/// was written with, and everything laid out from that line, such as an initializer's braces and the links of a chain,
/// follows it:
/// </para>
/// <code>
/// async () =>
/// await closeAdvertisement.ClickAsync(options)
///     .ConfigureAwait(continueOnCapturedContext: false)
/// </code>
/// <para>
/// A body that starts beside its arrow, <c>async () => await closeAdvertisement</c> with the chain's links below it,
/// has a line the other layout rules can measure from, and a long body breaks inside itself: at the links of its
/// chain, or at the arguments of its call. A lambda with a block keeps its brace where the rules for blocks put it,
/// and a comment between the arrow and the body stays where its author wrote it.
/// </para>
/// </remarks>
internal static class LambdaBodyPlacement
{
    public const string RuleIdentifier = "NETAGENTS0053";

    // The body starts on a later line than the arrow, with nothing but whitespace between the two.
    public static bool IsDetached(LambdaExpressionSyntax lambda, SourceText text)
    {
        if (lambda.ExpressionBody is not ExpressionSyntax body || lambda.ArrowToken.IsMissing)
            return false;

        // A comment between the arrow and the body is its author's, who decides which line it belongs to.
        SyntaxToken first = body.GetFirstToken();
        bool plain = IsWhitespace(lambda.ArrowToken.TrailingTrivia) && IsWhitespace(first.LeadingTrivia);

        return plain && text.Lines.GetLineFromPosition(lambda.ArrowToken.Span.End).LineNumber != text.Lines
            .GetLineFromPosition(first.SpanStart)
            .LineNumber;
    }

    // One space after the arrow, and nothing before the body: value => value.Length.
    public static SyntaxNode Join(SyntaxNode root, LambdaExpressionSyntax lambda)
    {
        SyntaxToken arrow = lambda.ArrowToken;
        SyntaxToken first = lambda.Body.GetFirstToken();

        return root.ReplaceTokens(tokens: [arrow, first],
            (original, rewritten) => original == arrow ? rewritten.WithTrailingTrivia(SyntaxFactory.Space) : rewritten.WithLeadingTrivia(SyntaxTriviaList.Empty));
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
