using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        string indentation = GetIndentation(source, list.SpanStart);
        TextLine firstLine = source.Lines[index: 0];

        string newline = firstLine.EndIncludingLineBreak > firstLine.End
            ? source.ToString(TextSpan.FromBounds(firstLine.End, firstLine.EndIncludingLineBreak))
            : "\n";

        List<TextChange> changes = [];

        foreach (SyntaxNode item in items)
        {
            SyntaxToken previous = item.GetFirstToken().GetPreviousToken();
            changes.Add(SeparateItems(source, previous.Span.End, item.SpanStart, newline, indentation + "    "));
        }

        SyntaxToken closing = list.GetLastToken();
        changes.Add(SeparateItems(source, closing.GetPreviousToken().Span.End, closing.SpanStart, newline, indentation));

        // Separating the statements that share a line lets the whitespace formatter lay out every block
        // around them, except where a lambda's body opens. It keeps that brace wherever it already starts
        // a line, and places one it moves there under the statement holding the call, so the brace is
        // put under the lambda here and the formatter aligns the body with it.
        foreach (SyntaxList<StatementSyntax> statements in GetStatementLists(list))
        {
            for (int index = 1; index < statements.Count; index++)
            {
                if (GetLine(source, statements[index].SpanStart) == GetLine(source, statements[index - 1].Span.End))
                    changes.Add(SeparateItems(source, statements[index - 1].Span.End, statements[index].SpanStart, newline, indentation + "    "));
            }
        }

        foreach (AnonymousFunctionExpressionSyntax function in GetSpreadLambdas(list))
        {
            if (OpensUnderLambda(function, source))
                continue;

            // A lambda passed as an item starts the item's new line; any other keeps the line it is on.
            string lambdaIndentation = function.Parent is ArgumentSyntax argument && argument.Parent == list
                ? indentation + "    "
                : GetIndentation(source, function.SpanStart);

            SyntaxToken brace = GetOpeningBrace(function);
            changes.Add(SeparateItems(source, brace.GetPreviousToken().Span.End, brace.SpanStart, newline, lambdaIndentation));
        }

        return [.. changes.OrderBy(static change => change.Span.Start)];
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

        if (GetLine(source, list.GetLastToken().SpanStart) <= GetLine(source, previousEnd))
            return false;

        foreach (SyntaxList<StatementSyntax> statements in GetStatementLists(list))
        {
            for (int index = 1; index < statements.Count; index++)
            {
                if (GetLine(source, statements[index].SpanStart) <= GetLine(source, statements[index - 1].Span.End))
                    return false;
            }
        }

        return GetSpreadLambdas(list).All(function => OpensUnderLambda(function, source));
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
        if (measurement.HasInlineComment || measurement.HasTokenLineBreak || measurement.HasStatementSequence)
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

    private static string GetIndentation(SourceText source, int position)
    {
        TextLine line = source.Lines.GetLineFromPosition(position);

        return new([.. source.ToString(line.Span).TakeWhile(static character => character is ' ' or '\t')]);
    }

    private static int GetLine(SourceText source, int position)
    {
        return source.Lines.GetLineFromPosition(position).LineNumber;
    }

    private static SyntaxToken GetOpeningBrace(AnonymousFunctionExpressionSyntax function)
    {
        return function.Block?.OpenBraceToken ?? throw new InvalidOperationException(message: "A spread lambda has no block body.");
    }

    // The lambdas and anonymous methods whose bodies hold a statement list this list lays out, directly
    // or in a block nested inside them.
    private static AnonymousFunctionExpressionSyntax[] GetSpreadLambdas(SyntaxNode list)
    {
        return [.. GetStatementLists(list)
            .SelectMany(statements => statements[index: 0].Ancestors().TakeWhile(ancestor => ancestor != list))
            .OfType<AnonymousFunctionExpressionSyntax>()
            .Where(static function => function.Block is not null)
            .Distinct()];
    }

    // The statement lists this list lays out: those it encloses most closely, so a block inside a
    // nested call is laid out by that call's list and never by two lists in one fix.
    private static IEnumerable<SyntaxList<StatementSyntax>> GetStatementLists(SyntaxNode list)
    {
        foreach (SyntaxNode node in list.DescendantNodes())
        {
            SyntaxList<StatementSyntax> statements = node switch
            {
                BlockSyntax block => block.Statements,
                SwitchSectionSyntax section => section.Statements,
                _ => default,
            };

            if (statements.Count > 1 && node.Ancestors().First(IsList) == list)
                yield return statements;
        }
    }

    // Statements written in sequence never share one line inside an argument: NETAGENTS0016 puts
    // blank lines between some of them, which joining the list would undo on every pass (#38).
    private static bool HoldsStatementSequence(SyntaxToken token)
    {
        return token.Parent switch
        {
            BlockSyntax block => token.IsKind(SyntaxKind.OpenBraceToken) && block.Statements.Count > 1,
            SwitchLabelSyntax { Parent: SwitchSectionSyntax section } => section.Statements.Count > 1,
            _ => false,
        };
    }

    // One walk over the tokens answers everything the bracket needs: their combined width, how many
    // gaps normalizing could widen, a line break written inside a literal, statements written in
    // sequence, and the comments that either force multiple lines outright or put the compact text
    // beyond the bracket.
    private static LineFitMeasurement Measure(SyntaxNode list)
    {
        bool blockComment = false;
        bool inlineComment = false;
        bool lineBreak = false;
        bool statementSequence = false;
        int tokens = 0;
        int width = 0;

        foreach (SyntaxToken token in list.DescendantTokens())
        {
            tokens++;
            width += token.Text.Length;
            statementSequence = statementSequence || HoldsStatementSequence(token);

            if (!lineBreak && token.Text.IndexOfAny(LineBreaks) >= 0)
                lineBreak = true;

            foreach (SyntaxTrivia trivia in token.LeadingTrivia)
                Classify(list, trivia, ref blockComment, ref inlineComment);

            foreach (SyntaxTrivia trivia in token.TrailingTrivia)
                Classify(list, trivia, ref blockComment, ref inlineComment);
        }

        return new(blockComment, inlineComment, lineBreak, statementSequence, tokens, width);
    }

    // The body opens on a line of its own, indented as deep as the line the lambda starts on.
    private static bool OpensUnderLambda(AnonymousFunctionExpressionSyntax function, SourceText source)
    {
        SyntaxToken brace = GetOpeningBrace(function);

        return GetLine(source, brace.SpanStart) > GetLine(source, brace.GetPreviousToken().Span.End)
            && GetIndentation(source, brace.SpanStart) == GetIndentation(source, function.SpanStart);
    }

    private static TextChange SeparateItems(SourceText source, int start, int end, string newline, string indentation)
    {
        TextSpan span = TextSpan.FromBounds(start, end);
        string comments = source.ToString(span).Trim();
        string replacement = comments.Length == 0 ? newline + indentation : newline + indentation + comments + newline + indentation;

        return new TextChange(span, replacement);
    }

    private readonly struct LineFitMeasurement(bool blockComment, bool inlineComment, bool lineBreak, bool statementSequence, int tokens, int width)
    {
        public bool HasBlockComment { get; } = blockComment;

        public bool HasInlineComment { get; } = inlineComment;

        public bool HasStatementSequence { get; } = statementSequence;

        public bool HasTokenLineBreak { get; } = lineBreak;

        public int Tokens { get; } = tokens;

        public int Width { get; } = width;
    }
}
