using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting.Collections;

namespace NetAgents.Analyzers.Formatting;

internal static class ArgumentLayout
{
    internal const string RuleIdentifier = "NETAGENTS0020";

    private static readonly char[] LineBreaks = ['\r', '\n', '\u0085', '\u2028', '\u2029'];

    // Joins a delimiter and its neighbouring item, unless a comment between them keeps them on separate lines.
    internal static TextChange Attach(SourceText source, int start, int end, string newline, string indentation)
    {
        return HasComment(source, start, end)
            ? SeparateItems(source, start, end, newline, indentation)
            : new TextChange(TextSpan.FromBounds(start, end), string.Empty);
    }

    internal static bool Breaks(SourceText source, int start, int end)
    {
        return GetLine(source, end) > GetLine(source, start);
    }

    internal static bool CanFix(SyntaxNode list)
    {
        return !list.ContainsDirectives && list.DescendantTrivia().All(IsWhitespaceOrComment);
    }

    internal static TextChange[] GetChanges(SyntaxNode list, SourceText source)
    {
        SyntaxNode[] items = [.. list.ChildNodes()];

        if (items.Length == 0 || !RequiresMultipleLines(list))
            return [new TextChange(list.Span, GetCompactText(list))];

        string indentation = GetIndentation(source, list.SpanStart);
        string itemIndentation = GetItemIndentation(list, source);
        TextLine firstLine = source.Lines[index: 0];

        string newline = firstLine.EndIncludingLineBreak > firstLine.End
            ? source.ToString(TextSpan.FromBounds(firstLine.End, firstLine.EndIncludingLineBreak))
            : "\n";

        // The first item stays beside the opening delimiter, every later item starts a line of its own,
        // and the closing delimiter follows the last item.
        List<TextChange> changes = [Attach(source, list.GetFirstToken().Span.End, items[0].SpanStart, newline, itemIndentation)];

        for (int index = 1; index < items.Length; index++)
        {
            SyntaxToken previous = items[index].GetFirstToken().GetPreviousToken();

            changes.Add(SeparateItems(source, previous.Span.End, items[index].SpanStart, newline, itemIndentation));
        }

        SyntaxToken closing = list.GetLastToken();

        changes.Add(Attach(source, closing.GetPreviousToken().Span.End, closing.SpanStart, newline, indentation));

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
            string lambdaIndentation = GetLambdaIndentation(function, list, source);
            BlockSyntax body = GetBody(function);
            SyntaxToken opening = body.OpenBraceToken;
            SyntaxToken end = body.CloseBraceToken;

            if (!OpensUnderLambda(function, source))
                changes.Add(SeparateItems(source, opening.GetPreviousToken().Span.End, opening.SpanStart, newline, lambdaIndentation));

            if (body.Statements.Count > 0 && SharesLine(source, body.Statements[index: 0].GetFirstToken()))
                changes.Add(SeparateItems(source, opening.Span.End, body.Statements[index: 0].SpanStart, newline, lambdaIndentation + "    "));

            if (SharesLine(source, end))
                changes.Add(SeparateItems(source, end.GetPreviousToken().Span.End, end.SpanStart, newline, lambdaIndentation));
        }

        return [.. changes.OrderBy(static change => change.Span.Start)];
    }

    internal static string GetCompactText(SyntaxNode list)
    {
        return list.WithoutTrivia().NormalizeWhitespace(indentation: "", eol: " ").ToFullString();
    }

    internal static string GetIndentation(SourceText source, int position)
    {
        TextLine line = source.Lines.GetLineFromPosition(position);

        return new(value: [.. source.ToString(line.Span).TakeWhile(static character => character is ' ' or '\t')]);
    }

    internal static int GetLine(SourceText source, int position)
    {
        return source.Lines.GetLineFromPosition(position).LineNumber;
    }

    internal static bool HasComment(SourceText source, int start, int end)
    {
        return source.ToString(TextSpan.FromBounds(start, end)).Trim().Length > 0;
    }

    internal static bool HasExpectedLayout(SyntaxNode list, SourceText source)
    {
        SyntaxNode[] items = [.. list.ChildNodes()];
        bool multiple = RequiresMultipleLines(list);

        if (items.Length == 0 && multiple)
            return true;

        if (!multiple)
            return GetLine(source, list.SpanStart) == GetLine(source, list.Span.End);

        int opening = list.GetFirstToken().Span.End;
        int first = items[0].SpanStart;
        string itemIndentation = GetItemIndentation(list, source);

        // The first item stays beside the opening delimiter, unless a comment between them moves it down.
        if (HasComment(source, opening, first) ? !StartsLine(source, opening, first, itemIndentation) : Breaks(source, opening, first))
            return false;

        for (int index = 1; index < items.Length; index++)
        {
            if (!StartsLine(source, items[index - 1].Span.End, items[index].SpanStart, itemIndentation))
                return false;
        }

        int last = items[items.Length - 1].Span.End;
        int closing = list.GetLastToken().SpanStart;

        if (Breaks(source, last, closing) != HasComment(source, last, closing))
            return false;

        foreach (SyntaxList<StatementSyntax> statements in GetStatementLists(list))
        {
            for (int index = 1; index < statements.Count; index++)
            {
                if (GetLine(source, statements[index].SpanStart) <= GetLine(source, statements[index - 1].Span.End))
                    return false;
            }
        }

        return GetSpreadLambdas(list).All(function => OpensUnderLambda(function, source) && !CrowdsBody(function, source));
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
        if (measurement.HasInlineComment || measurement.HasTokenLineBreak || measurement.HasSpreadStatement)
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

    internal static TextChange SeparateItems(SourceText source, int start, int end, string newline, string indentation)
    {
        TextSpan span = TextSpan.FromBounds(start, end);
        string comments = source.ToString(span).Trim();
        string replacement = comments.Length == 0 ? newline + indentation : newline + indentation + comments + newline + indentation;

        return new TextChange(span, replacement);
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

    // The body's first statement or its closing brace shares a line with the token before it.
    private static bool CrowdsBody(AnonymousFunctionExpressionSyntax function, SourceText source)
    {
        BlockSyntax body = GetBody(function);

        return (body.Statements.Count > 0 && SharesLine(source, body.Statements[index: 0].GetFirstToken())) || SharesLine(source, body.CloseBraceToken);
    }

    private static BlockSyntax GetBody(AnonymousFunctionExpressionSyntax function)
    {
        return function.Block ?? throw new InvalidOperationException(message: "A spread lambda has no block body.");
    }

    // Later items sit one level deeper than the line the list starts on, and one more for each enclosing
    // list that opens on that line and continues past it, so nested lists never share an indentation.
    private static string GetItemIndentation(SyntaxNode list, SourceText source)
    {
        int line = GetLine(source, list.SpanStart);
        int depth = 1 + list.Ancestors().Count(ancestor => SpreadsFromLine(ancestor, source, line));

        return GetIndentation(source, list.SpanStart) + new string(c: ' ', 4 * depth);
    }

    // A lambda on its item's first line moves with the item, and one on a later line keeps the line it is on.
    private static string GetLambdaIndentation(AnonymousFunctionExpressionSyntax function, SyntaxNode list, SourceText source)
    {
        SyntaxNode[] items = [.. list.ChildNodes()];
        int index = Array.FindIndex(items, item => item.Span.Contains(function.Span));
        SyntaxNode item = items[index];

        if (GetLine(source, function.SpanStart) != GetLine(source, item.SpanStart))
            return GetIndentation(source, function.SpanStart);

        bool attached = index == 0 && !HasComment(source, item.GetFirstToken().GetPreviousToken().Span.End, item.SpanStart);

        return attached ? GetIndentation(source, list.SpanStart) : GetItemIndentation(list, source);
    }

    // The lambdas and anonymous methods this list encloses most closely whose bodies hold a statement
    // that spreads over lines, directly or in a block or call nested inside them.
    private static AnonymousFunctionExpressionSyntax[] GetSpreadLambdas(SyntaxNode list)
    {
        return [.. list.DescendantNodes()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .Where(function => function.Block is BlockSyntax body && function.Ancestors().First(IsList) == list
                    && body.DescendantTokens().Any(HoldsSpreadStatement))];
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

    // Some statements never share one line inside an argument, and joining the list would undo their
    // layout on every pass. NETAGENTS0016 puts blank lines between statements written in sequence (#38),
    // NETAGENTS0017 gives every brace of a statement block its own line, IDE2001 does the same for the
    // body of a statement that holds others (#49), and an object initializer puts every element on a
    // line of its own (#55).
    private static bool HoldsSpreadStatement(SyntaxToken token)
    {
        return token.Parent switch
        {
            BlockSyntax block => token.IsKind(SyntaxKind.OpenBraceToken) && (block.Statements.Count > 1 || ControlFlowBraces.BelongsToStatement(block)),
            SwitchLabelSyntax { Parent: SwitchSectionSyntax section } => section.Statements.Count > 1,
            IfStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
                or UsingStatementSyntax or LockStatementSyntax or FixedStatementSyntax or TryStatementSyntax
                or SwitchStatementSyntax or CheckedStatementSyntax or UnsafeStatementSyntax => token == token.Parent
                    .GetFirstToken(),
            SyntaxNode braced when braced is InitializerExpressionSyntax or AnonymousObjectCreationExpressionSyntax
                => token.IsKind(SyntaxKind.OpenBraceToken) && CollectionLayout.AlwaysExpands(braced),
            _ => false,
        };
    }

    private static bool IsWhitespaceOrComment(SyntaxTrivia trivia)
    {
        return trivia.Kind() is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia
            or SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia;
    }

    // One walk over the tokens answers everything the bracket needs: their combined width, how many
    // gaps normalizing could widen, a line break written inside a literal, statements that spread
    // over lines, and the comments that either force multiple lines outright or put the compact text
    // beyond the bracket.
    private static LineFitMeasurement Measure(SyntaxNode list)
    {
        bool blockComment = false;
        bool inlineComment = false;
        bool lineBreak = false;
        bool spreadStatement = false;

        // The compact width: the text of every token, and the gaps between them that normalizing could widen.
        int tokens = 0;
        int width = 0;

        foreach (SyntaxToken token in list.DescendantTokens())
        {
            tokens++;
            width += token.Text.Length;
            spreadStatement = spreadStatement || HoldsSpreadStatement(token);

            if (!lineBreak && token.Text.IndexOfAny(LineBreaks) >= 0)
                lineBreak = true;

            foreach (SyntaxTrivia trivia in token.LeadingTrivia)
                Classify(list, trivia, ref blockComment, ref inlineComment);

            foreach (SyntaxTrivia trivia in token.TrailingTrivia)
                Classify(list, trivia, ref blockComment, ref inlineComment);
        }

        return new(blockComment, inlineComment, lineBreak, spreadStatement, tokens, width);
    }

    // The body opens on a line of its own, indented as deep as the line the lambda starts on.
    private static bool OpensUnderLambda(AnonymousFunctionExpressionSyntax function, SourceText source)
    {
        SyntaxToken brace = GetBody(function).OpenBraceToken;

        return GetLine(source, brace.SpanStart) > GetLine(source, brace.GetPreviousToken().Span.End)
            && GetIndentation(source, brace.SpanStart) == GetIndentation(source, function.SpanStart);
    }

    private static bool SharesLine(SourceText source, SyntaxToken token)
    {
        return GetLine(source, token.SpanStart) == GetLine(source, token.GetPreviousToken().Span.End);
    }

    // A list that opens on the given line and holds more than one item, so it continues past that line.
    private static bool SpreadsFromLine(SyntaxNode node, SourceText source, int line)
    {
        return IsList(node) && node.ChildNodes().Skip(count: 1).Any() && GetLine(source, node.SpanStart) == line;
    }

    // The item starts a later line than the token before it, at the indentation given.
    private static bool StartsLine(SourceText source, int previousEnd, int start, string indentation)
    {
        return GetLine(source, start) > GetLine(source, previousEnd) && GetIndentation(source, start) == indentation;
    }

    private readonly struct LineFitMeasurement(bool blockComment, bool inlineComment, bool lineBreak, bool spreadStatement, int tokens, int width)
    {
        // What keeps the list off one line whatever its width: a comment inside it, a statement spreading over lines,
        // or a line break written inside a token such as a verbatim string.
        public bool HasBlockComment { get; } = blockComment;
        public bool HasInlineComment { get; } = inlineComment;
        public bool HasSpreadStatement { get; } = spreadStatement;
        public bool HasTokenLineBreak { get; } = lineBreak;

        // The compact width: the text of every token, and how many gaps between them normalizing could widen.
        public int Tokens { get; } = tokens;
        public int Width { get; } = width;
    }
}
