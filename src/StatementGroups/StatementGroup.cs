using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.StatementGroups;

/// <summary>
/// Describes a run of statements of one kind that no blank line may split.
/// </summary>
/// <param name="first">The statement that opens the group.</param>
/// <param name="last">The statement that closes the group.</param>
/// <param name="kind">The kind every statement in the group shares.</param>
/// <param name="count">How many statements the group holds.</param>
internal sealed class StatementGroup(StatementSyntax first, StatementSyntax last, StatementKind kind, int count)
{
    public int Count { get; } = count;

    public StatementSyntax First { get; } = first;

    public StatementKind Kind { get; } = kind;

    public StatementSyntax Last { get; } = last;
}
