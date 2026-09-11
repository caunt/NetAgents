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
            {
                continue;
            }

            int previousLine = source.Lines.GetLineFromPosition(previous.Span.End).LineNumber;
            int currentLine = source.Lines.GetLineFromPosition(statement.SpanStart).LineNumber;

            if (HasBlankLine(previous, statement, source, previousLine, currentLine))
            {
                continue;
            }

            string lineEnding = GetLineEnding(source, previousLine);
            int insertionPosition = source.Lines.GetLineFromPosition(statement.FullSpan.Start).Start;
            TextChange change;

            if (insertionPosition > previous.Span.End)
            {
                change = new TextChange(new TextSpan(insertionPosition, length: 0), lineEnding);
            }
            else
            {
                int whitespaceStart = statement.SpanStart;

                while (whitespaceStart > previous.Span.End && source[whitespaceStart - 1] is ' ' or '\t')
                {
                    whitespaceStart--;
                }

                TextLine line = source.Lines[currentLine];
                int indentationEnd = line.Start;

                while (indentationEnd < line.End && source[indentationEnd] is ' ' or '\t')
                {
                    indentationEnd++;
                }

                string indentation = source.ToString(TextSpan.FromBounds(line.Start, indentationEnd));
                change = new TextChange(TextSpan.FromBounds(whitespaceStart, statement.SpanStart), lineEnding + lineEnding + indentation);
            }

            changes.Add(statement.GetFirstToken().Span, change);
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
                int position = compilation.Members.IndexOf(global);

                return position > 0 && compilation.Members[position - 1] is GlobalStatementSyntax previous ? previous.Statement : null;
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
            || statement is LocalDeclarationStatementSyntax declaration
                && (declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
                    || source.Lines.GetLineFromPosition(declaration.SpanStart).LineNumber
                        != source.Lines.GetLineFromPosition(declaration.Span.End).LineNumber);
    }

    private static bool HasBlankLine(StatementSyntax previous, StatementSyntax current, SourceText source, int previousLine, int currentLine)
    {
        for (int lineNumber = previousLine + 1; lineNumber < currentLine; lineNumber++)
        {
            TextLine line = source.Lines[lineNumber];

            if (source.ToString(line.Span).All(char.IsWhiteSpace)
                && !IsInsideContentTrivia(previous.GetTrailingTrivia(), line.Start)
                && !IsInsideContentTrivia(current.GetLeadingTrivia(), line.Start))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideContentTrivia(SyntaxTriviaList triviaList, int position)
    {
        foreach (SyntaxTrivia trivia in triviaList)
        {
            if (trivia.Span.Contains(position) && !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetLineEnding(SourceText source, int preferredLine)
    {
        TextLine line = source.Lines[preferredLine];

        if (line.EndIncludingLineBreak > line.End)
        {
            return source.ToString(TextSpan.FromBounds(line.End, line.EndIncludingLineBreak));
        }

        foreach (TextLine candidate in source.Lines)
        {
            if (candidate.EndIncludingLineBreak > candidate.End)
            {
                return source.ToString(TextSpan.FromBounds(candidate.End, candidate.EndIncludingLineBreak));
            }
        }

        return "\n";
    }
}
