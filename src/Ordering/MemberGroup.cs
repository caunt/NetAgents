using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Ordering;

/// <summary>
/// Describes a run of fields, properties, or events of one bucket that no blank line may split.
/// </summary>
/// <param name="first">The member that opens the group.</param>
/// <param name="last">The member that closes the group.</param>
/// <param name="description">The plural the diagnostic names the members with, such as "fields".</param>
/// <param name="count">How many members the group holds.</param>
internal sealed class MemberGroup(MemberDeclarationSyntax first, MemberDeclarationSyntax last, string description, int count)
{
    public int Count { get; } = count;
    public string Description { get; } = description;
    public MemberDeclarationSyntax First { get; } = first;
    public MemberDeclarationSyntax Last { get; } = last;
}
