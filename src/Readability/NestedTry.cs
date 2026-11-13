using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Finds a <c>try</c> statement that only wraps another one so its <c>finally</c> can run, which one statement says
/// directly.
/// </summary>
/// <remarks>
/// <code>
/// try
/// {
///     try { Run(); }
///     catch (IOException exception) { Log(exception); }
/// }
/// finally { Release(); }
/// </code>
/// <para>
/// behaves exactly like <c>try { Run(); } catch (IOException exception) { Log(exception); } finally { Release(); }</c>:
/// a handler runs before the cleanup either way, and the cleanup runs after a handler that throws. Only that shape
/// merges. An outer statement with handlers of its own would also catch what the inner handlers throw, and an inner
/// <c>finally</c> would run before the outer handlers, so neither merges without changing what the code does.
/// </para>
/// </remarks>
internal static class NestedTry
{
    public const string RuleIdentifier = "NETAGENTS0049";

    // The try statement this one only wraps, when the two merge into one without changing when anything runs.
    public static TryStatementSyntax? GetMergeable(TryStatementSyntax outer)
    {
        if (outer.Catches.Count > 0 || outer.Finally is null || outer.Block.Statements.Count != 1)
            return null;

        if (outer.Block.Statements[index: 0] is not TryStatementSyntax inner || inner.Catches.Count == 0 || inner.Finally is not null)
            return null;

        // A comment or directive between the two statements belongs to its author, who decides where it goes.
        bool plain = IsPlain(outer.Block.OpenBraceToken.TrailingTrivia) && IsPlain(inner.GetLeadingTrivia());

        return plain && IsPlain(inner.GetTrailingTrivia()) && IsPlain(outer.Block.CloseBraceToken.LeadingTrivia) ? inner : null;
    }

    // The outer statement keeps its own trivia and finally clause, and takes the inner block and handlers.
    public static TryStatementSyntax Merge(TryStatementSyntax outer, TryStatementSyntax inner)
    {
        return outer.WithBlock(inner.Block).WithCatches(inner.Catches);
    }

    private static bool IsPlain(SyntaxTriviaList trivia)
    {
        foreach (SyntaxTrivia piece in trivia)
        {
            if (!piece.IsKind(SyntaxKind.WhitespaceTrivia) && !piece.IsKind(SyntaxKind.EndOfLineTrivia))
                return false;
        }

        return true;
    }
}
