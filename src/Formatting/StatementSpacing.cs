using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting;

internal static class StatementSpacing
{
    public const string RuleIdentifier = "NETAGENTS0016";

    public static ImmutableDictionary<TextSpan, TextChange> GetChanges(SyntaxNode root, SourceText source)
    {
        ImmutableDictionary<TextSpan, TextChange>.Builder changes = ImmutableDictionary.CreateBuilder<TextSpan, TextChange>();

        foreach (StatementSyntax statement in root.DescendantNodes().OfType<StatementSyntax>())
        {
            StatementSyntax? previous = GetPreviousStatement(statement);

            if (previous is null || (!RequiresSeparation(previous, source) && !RequiresSeparation(statement, source)))
                continue;

            TextChange? change = DeclarationSpacing.GetChange(previous, statement, source);

            if (change is TextChange spacingChange)
                changes.Add(statement.GetFirstToken().Span, spacingChange);
        }

        return changes.ToImmutable();
    }

    private static StatementSyntax? GetPreviousStatement(StatementSyntax statement)
    {
        switch (statement.Parent)
        {
            case BlockSyntax block:
                return GetPreviousStatement(block.Statements, statement);
            case SwitchSectionSyntax section:
                return GetPreviousStatement(section.Statements, statement);
            case GlobalStatementSyntax global when global.Parent is CompilationUnitSyntax compilation:
                {
                    int position = compilation.Members.IndexOf(global);

                    return position > 0 && compilation.Members[position - 1] is GlobalStatementSyntax previous ? previous.Statement : null;
                }
            default:
                return null;
        }
    }

    private static StatementSyntax? GetPreviousStatement(SyntaxList<StatementSyntax> statements, StatementSyntax statement)
    {
        int position = statements.IndexOf(statement);

        return position > 0 ? statements[position - 1] : null;
    }

    private static bool RequiresSeparation(StatementSyntax statement, SourceText source)
    {
        return statement is IfStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax
            or WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax or TryStatementSyntax
            or UsingStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax or BreakStatementSyntax
            or ContinueStatementSyntax or YieldStatementSyntax
            || (statement is LocalDeclarationStatementSyntax declaration
                && (declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
                    || source.Lines.GetLineFromPosition(declaration.SpanStart).LineNumber
                        != source.Lines.GetLineFromPosition(declaration.Span.End).LineNumber));
    }
}
