using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Formatting.Chains;

/// <summary>
/// One link of a member chain: a field, property, event, or method reached through a dot, or a namespace or type
/// qualifier that belongs to the receiver.
/// </summary>
/// <param name="access">The member access, from the dot to the member's name.</param>
/// <param name="qualifier">Whether the link names a namespace or a type, as <c>Console</c> does in
/// <c>System.Console.Error</c>, and so never starts a line.</param>
internal sealed class ChainLink(MemberAccessExpressionSyntax access, bool qualifier)
{
    public MemberAccessExpressionSyntax Access { get; } = access;
    public SyntaxToken Dot => Access.OperatorToken;
    public bool IsQualifier { get; } = qualifier;
}
