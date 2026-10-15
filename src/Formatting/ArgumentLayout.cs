using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting;

internal static class ArgumentLayout
{
    internal const string RuleIdentifier = "NETAGENTS0020";

    private static readonly char[] LineBreaks = ['\r', '\n', '\u0085', '\u2028', '\u2029'];

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
        bool multiple = RequiresMultipleLines(list);

        if (items.Length == 0 && multiple)
            return true;

        if (!multiple)
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
        LineFitMeasurement measurement = Measure(list);

        // Only trivia the compact text reproduces matters; trivia outside the delimiters stays where it is.
        if (measurement.HasInlineComment || measurement.HasTokenLineBreak)
            return true;

        // Normalizing keeps every token and puts at most one space between neighbours, so the token
        // widths bracket the compact length and usually settle the comparison without building it.
        // A block comment is the exception: WithoutTrivia only strips the list's own outer trivia,
        // so a comment on an inner token survives into that text and outruns the upper bound.
        if (!measurement.HasBlockComment)
        {
            if (measurement.Width - 2 > LayoutLimits.MaximumInlineLength)
                return true;

            if (measurement.Width + measurement.Tokens - 2 <= LayoutLimits.MaximumInlineLength)
                return false;
        }

        string compact = GetCompactText(list);

        return compact.Length - 2 > LayoutLimits.MaximumInlineLength || compact.IndexOfAny(LineBreaks) >= 0;
    }

    private static void Classify(SyntaxNode list, SyntaxTrivia trivia, ref bool blockComment, ref bool inlineComment)
    {
        if (!list.Span.Contains(trivia.SpanStart))
            return;

        SyntaxKind kind = trivia.Kind();
        bool documentation = kind is SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia;

        if (kind is SyntaxKind.SingleLineCommentTrivia)
            inlineComment = true;
        else if (documentation || kind is SyntaxKind.MultiLineCommentTrivia)
            blockComment = true;
    }

    private static int GetLine(SourceText source, int position)
    {
        return source.Lines.GetLineFromPosition(position).LineNumber;
    }

    // One walk over the tokens answers everything the bracket needs: their combined width, how many
    // gaps normalizing could widen, a line break written inside a literal, and the comments that
    // either force multiple lines outright or put the compact text beyond the bracket.
    private static LineFitMeasurement Measure(SyntaxNode list)
    {
        bool blockComment = false;
        bool inlineComment = false;
        bool lineBreak = false;
        int tokens = 0;
        int width = 0;

        foreach (SyntaxToken token in list.DescendantTokens())
        {
            tokens++;
            width += token.Text.Length;

            if (!lineBreak && token.Text.IndexOfAny(LineBreaks) >= 0)
                lineBreak = true;

            foreach (SyntaxTrivia trivia in token.LeadingTrivia)
                Classify(list, trivia, ref blockComment, ref inlineComment);

            foreach (SyntaxTrivia trivia in token.TrailingTrivia)
                Classify(list, trivia, ref blockComment, ref inlineComment);
        }

        return new(blockComment, inlineComment, lineBreak, tokens, width);
    }

    private static TextChange SeparateItems(SourceText source, int start, int end, string newline, string indentation)
    {
        TextSpan span = TextSpan.FromBounds(start, end);
        string comments = source.ToString(span).Trim();
        string replacement = comments.Length == 0 ? newline + indentation : newline + indentation + comments + newline + indentation;

        return new TextChange(span, replacement);
    }

    private readonly struct LineFitMeasurement(bool blockComment, bool inlineComment, bool lineBreak, int tokens, int width)
    {
        public bool HasBlockComment { get; } = blockComment;

        public bool HasInlineComment { get; } = inlineComment;

        public bool HasTokenLineBreak { get; } = lineBreak;

        public int Tokens { get; } = tokens;

        public int Width { get; } = width;
    }
}
