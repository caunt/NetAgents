using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting;

internal static class ArgumentLayout
{
    internal const string RuleIdentifier = "NETAGENTS0020";

    internal static bool CanFix(SyntaxNode list)
    {
        return !list.ContainsDirectives && list.DescendantTrivia().All(
            static trivia =>
            trivia.Kind() is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia
                or SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
        );
    }

    internal static TextChange[] GetChanges(SyntaxNode list, SourceText source)
    {
        SyntaxNode[] items = [.. list.ChildNodes()];

        if (items.Length == 0 || !RequiresMultipleLines(list))
            return [new TextChange(list.Span, GetCompactText(list))];

        TextLine line = source.Lines.GetLineFromPosition(list.SpanStart);
        string indentation = new([.. source.ToString(line.Span).TakeWhile(static character => character is ' ' or '\t')]);
        TextLine firstLine = source.Lines[index: 0];

        string newline = firstLine.EndIncludingLineBreak > firstLine.End
            ? source.ToString(TextSpan.FromBounds(firstLine.End, firstLine.EndIncludingLineBreak))
            : "\n";

        TextChange[] changes = new TextChange[items.Length + 1];

        for (int index = 0; index < items.Length; index++)
        {
            SyntaxToken previous = items[index].GetFirstToken().GetPreviousToken();
            changes[index] = SeparateItems(source, previous.Span.End, items[index].SpanStart, newline, indentation + "    ");
        }

        SyntaxToken closing = list.GetLastToken();
        changes[items.Length] = SeparateItems(source, closing.GetPreviousToken().Span.End, closing.SpanStart, newline, indentation);

        return changes;
    }

    internal static string GetCompactText(SyntaxNode list)
    {
        return list.WithoutTrivia().NormalizeWhitespace(indentation: "", eol: " ").ToFullString();
    }

    internal static bool HasExpectedLayout(SyntaxNode list, SourceText source)
    {
        SyntaxNode[] items = [.. list.ChildNodes()];

        if (items.Length == 0 && RequiresMultipleLines(list))
            return true;

        if (!RequiresMultipleLines(list))
            return GetLine(source, list.SpanStart) == GetLine(source, list.Span.End);

        int previousEnd = list.GetFirstToken().Span.End;

        foreach (SyntaxNode item in items)
        {
            if (GetLine(source, item.SpanStart) <= GetLine(source, previousEnd))
                return false;

            previousEnd = item.Span.End;
        }

        return GetLine(source, list.GetLastToken().SpanStart) > GetLine(source, previousEnd);
    }

    internal static bool IsList(SyntaxNode node)
    {
        return node.Kind() is SyntaxKind.ArgumentList or SyntaxKind.BracketedArgumentList
            or SyntaxKind.AttributeArgumentList or SyntaxKind.ParameterList or SyntaxKind.BracketedParameterList;
    }

    internal static bool RequiresMultipleLines(SyntaxNode list)
    {
        string compact = GetCompactText(list);

        return compact.Length - 2 > LayoutLimits.MaximumInlineLength
            || compact.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) >= 0
            || list.DescendantTrivia().Any(static trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));
    }

    private static int GetLine(SourceText source, int position)
    {
        return source.Lines.GetLineFromPosition(position).LineNumber;
    }

    private static TextChange SeparateItems(SourceText source, int start, int end, string newline, string indentation)
    {
        TextSpan span = TextSpan.FromBounds(start, end);
        string comments = source.ToString(span).Trim();
        string replacement = comments.Length == 0 ? newline + indentation : newline + indentation + comments + newline + indentation;

        return new TextChange(span, replacement);
    }
}
