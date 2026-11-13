using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Formatting.Chains;

/// <summary>
/// One link of a member chain: a member reached through a dot, and whether the chain calls it.
/// </summary>
/// <param name="access">The member access, from the dot to the member's name.</param>
/// <param name="call">Whether the member is invoked, as in <c>.Select(…)</c>, rather than read, as in <c>.Values</c>.</param>
internal sealed class ChainLink(MemberAccessExpressionSyntax access, bool call)
{
    public MemberAccessExpressionSyntax Access { get; } = access;
    public SyntaxToken Dot => Access.OperatorToken;
    public bool IsCall { get; } = call;
}
