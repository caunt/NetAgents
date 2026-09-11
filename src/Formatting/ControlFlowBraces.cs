using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting;

internal static class ControlFlowBraces
{
    public const string RuleIdentifier = "NETAGENTS0017";

    public static ImmutableDictionary<TextSpan, SyntaxNode> GetChanges(SyntaxNode root, SourceText source)
    {
        ImmutableDictionary<TextSpan, SyntaxNode>.Builder changes = ImmutableDictionary.CreateBuilder<TextSpan, SyntaxNode>();

        foreach (SyntaxNode node in root.DescendantNodes())
        {
            bool needsChange = node switch
            {
                BlockSyntax block => CanRemove(block, source),
                StatementSyntax statement => RequiresBraces(statement, source),
                SwitchSectionSyntax section => RequiresCaseBraces(section, source),
                _ => false,
            };

            if (needsChange)
                changes.Add(node.GetFirstToken().Span, node);
        }

        return changes.ToImmutable();
    }

    private static bool CanRemove(BlockSyntax block, SourceText source)
    {
        if (!HasSingleLineBody(block, source))
            return false;

        return block.Parent switch
        {
            IfStatementSyntax conditional => CanRemoveFromChain(conditional, source),
            ElseClauseSyntax { Parent: IfStatementSyntax conditional } => CanRemoveFromChain(conditional, source),
            ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
                or UsingStatementSyntax or LockStatementSyntax or FixedStatementSyntax
                or SwitchSectionSyntax or BlockSyntax or LabeledStatementSyntax => true,
            _ => false,
        };
    }

    private static bool RequiresBraces(StatementSyntax statement, SourceText source)
    {
        if (statement.ContainsDiagnostics || statement.Parent is null || statement.Parent.ContainsDirectives)
            return false;

        return statement.Parent switch
        {
            IfStatementSyntax conditional => !CanRemoveFromChain(conditional, source),
            ElseClauseSyntax { Parent: IfStatementSyntax conditional } when statement is not IfStatementSyntax
                => !CanRemoveFromChain(conditional, source),
            ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
                or UsingStatementSyntax or LockStatementSyntax or FixedStatementSyntax or LabeledStatementSyntax => !IsSingleLine(statement, source),
            _ => false,
        };
    }

    private static bool RequiresCaseBraces(SwitchSectionSyntax section, SourceText source)
    {
        if (section.ContainsDiagnostics || section.ContainsDirectives || section.Statements.Count == 0
            || section.Statements.Count == 1 && (section.Statements[index: 0] is BlockSyntax || IsSingleLine(section.Statements[index: 0], source)))
            return false;

        // A case block must not hide a variable or local function used by another section.
        ImmutableHashSet<string> declarations = ImmutableHashSet.Create<string>(System.StringComparer.Ordinal);

        foreach (StatementSyntax statement in section.Statements)
        {
            foreach (SyntaxNode node in statement.DescendantNodesAndSelf())
            {
                string name = node switch
                {
                    VariableDeclaratorSyntax variable => variable.Identifier.ValueText,
                    SingleVariableDesignationSyntax variable => variable.Identifier.ValueText,
                    LocalFunctionStatementSyntax function => function.Identifier.ValueText,
                    LabeledStatementSyntax labeled => labeled.Identifier.ValueText,
                    _ => string.Empty,
                };

                if (name.Length > 0)
                    declarations = declarations.Add(name);
            }
        }

        return section.Parent is not SwitchStatementSyntax selection
            || !selection.DescendantNodes().OfType<SimpleNameSyntax>()
                .Any(identifier => !section.Span.Contains(identifier.Span) && declarations.Contains(identifier.Identifier.ValueText));
    }

    private static bool HasSingleLineBody(BlockSyntax block, SourceText source)
    {
        if (block.ContainsDiagnostics || block.ContainsDirectives || block.Statements.Count != 1)
            return false;

        StatementSyntax statement = block.Statements[index: 0];

        // Declarations must retain their scope, including out variables and pattern captures.
        if (statement is BlockSyntax or LocalDeclarationStatementSyntax or LocalFunctionStatementSyntax
            or LabeledStatementSyntax or EmptyStatementSyntax
            || statement.DescendantNodes().OfType<VariableDesignationSyntax>().Any())
            return false;

        return IsSingleLine(statement, source) && !ExposesDanglingElse(block, statement);
    }

    private static bool CanRemoveFromChain(IfStatementSyntax conditional, SourceText source)
    {
        while (conditional.Parent is ElseClauseSyntax { Parent: IfStatementSyntax previous })
            conditional = previous;

        for (IfStatementSyntax? current = conditional; current is not null; current = current.Else?.Statement as IfStatementSyntax)
        {
            if (!CanRemainUnbraced(current.Statement, source))
                return false;

            if (current.Else is { Statement: not IfStatementSyntax } otherwise
                && !CanRemainUnbraced(otherwise.Statement, source))
                return false;
        }

        return true;
    }

    private static bool CanRemainUnbraced(StatementSyntax statement, SourceText source)
    {
        return statement is BlockSyntax block ? HasSingleLineBody(block, source) : IsSingleLine(statement, source);
    }

    private static bool IsSingleLine(StatementSyntax statement, SourceText source)
    {
        return source.Lines.GetLineFromPosition(statement.SpanStart).LineNumber
            == source.Lines.GetLineFromPosition(statement.Span.End - 1).LineNumber;
    }

    private static bool ExposesDanglingElse(BlockSyntax block, StatementSyntax statement)
    {
        if (!CanAbsorbElse(statement))
            return false;

        for (SyntaxNode? current = block; current is not null; current = current.Parent)
        {
            switch (current.Parent)
            {
                case IfStatementSyntax conditional when conditional.Statement == current && conditional.Else is not null:
                    return true;
                case IfStatementSyntax or ElseClauseSyntax or ForStatementSyntax or CommonForEachStatementSyntax
                    or WhileStatementSyntax or UsingStatementSyntax or LockStatementSyntax or FixedStatementSyntax:
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool CanAbsorbElse(StatementSyntax statement)
    {
        // Look through nested blocks too: Fix all may remove them in the same operation.
        return statement switch
        {
            IfStatementSyntax conditional => conditional.Else is null || CanAbsorbElse(conditional.Else.Statement),
            BlockSyntax { Statements.Count: 1 } block => CanAbsorbElse(block.Statements[index: 0]),
            ForStatementSyntax loop => CanAbsorbElse(loop.Statement),
            CommonForEachStatementSyntax loop => CanAbsorbElse(loop.Statement),
            WhileStatementSyntax loop => CanAbsorbElse(loop.Statement),
            UsingStatementSyntax resource => CanAbsorbElse(resource.Statement),
            LockStatementSyntax synchronization => CanAbsorbElse(synchronization.Statement),
            FixedStatementSyntax pinned => CanAbsorbElse(pinned.Statement),
            _ => false,
        };
    }
}
