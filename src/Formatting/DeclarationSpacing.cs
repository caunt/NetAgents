using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting;

internal static class DeclarationSpacing
{
    public static TextChange? GetChange(SyntaxNode previous, SyntaxNode current, SourceText source)
    {
        int previousLine = source.Lines.GetLineFromPosition(previous.Span.End).LineNumber;
        int currentLine = source.Lines.GetLineFromPosition(current.SpanStart).LineNumber;

        if (HasBlankLine(previous, current, source, previousLine, currentLine))
            return null;

        string lineEnding = GetLineEnding(source, previousLine);
        int insertionPosition = source.Lines.GetLineFromPosition(current.FullSpan.Start).Start;
        TextChange change;

        if (insertionPosition > previous.Span.End)
        {
            change = new TextChange(new TextSpan(insertionPosition, length: 0), lineEnding);
        }
        else
        {
            int whitespaceStart = current.SpanStart;

            while (whitespaceStart > previous.Span.End && source[whitespaceStart - 1] is ' ' or '\t')
                whitespaceStart--;

            TextLine line = source.Lines[currentLine];
            int indentationEnd = line.Start;

            while (indentationEnd < line.End && source[indentationEnd] is ' ' or '\t')
                indentationEnd++;

            string indentation = source.ToString(TextSpan.FromBounds(line.Start, indentationEnd));
            change = new TextChange(TextSpan.FromBounds(whitespaceStart, current.SpanStart), lineEnding + lineEnding + indentation);
        }

        return change;
    }

    private static string GetLineEnding(SourceText source, int preferredLine)
    {
        TextLine line = source.Lines[preferredLine];

        if (line.EndIncludingLineBreak > line.End)
            return source.ToString(TextSpan.FromBounds(line.End, line.EndIncludingLineBreak));

        foreach (TextLine candidate in source.Lines)
        {
            if (candidate.EndIncludingLineBreak > candidate.End)
                return source.ToString(TextSpan.FromBounds(candidate.End, candidate.EndIncludingLineBreak));
        }

        return "\n";
    }

    private static bool HasBlankLine(SyntaxNode previous, SyntaxNode current, SourceText source, int previousLine, int currentLine)
    {
        for (int lineNumber = previousLine + 1; lineNumber < currentLine; lineNumber++)
        {
            TextLine line = source.Lines[lineNumber];

            bool isBlankLine = source.ToString(line.Span).All(char.IsWhiteSpace)
                && !IsInsideContentTrivia(previous.GetTrailingTrivia(), line.Start)
                && !IsInsideContentTrivia(current.GetLeadingTrivia(), line.Start);

            if (isBlankLine)
                return true;
        }

        return false;
    }

    private static bool IsInsideContentTrivia(SyntaxTriviaList triviaList, int position)
    {
        foreach (SyntaxTrivia trivia in triviaList)
        {
            if (trivia.Span.Contains(position) && !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                return true;
        }

        return false;
    }
}
