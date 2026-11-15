using System.Collections.Generic;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting.Chains;
using NetAgents.Analyzers.Formatting.Collections;

namespace NetAgents.Analyzers.Formatting.Operators;

/// <summary>
/// Lays out <c>&amp;&amp;</c>, <c>||</c>, and <c>??</c> sequences and <c>?:</c> expressions: on one line when they fit
/// within 128 characters, otherwise one operand per line with the operator in front.
/// </summary>
/// <remarks>
/// <para>
/// A line too long for its statement breaks at its operators before it breaks anywhere else, so each operand gets a line
/// of its own and a call inside one is measured there instead of wrapping its arguments around the operator:
/// </para>
/// <code>
/// return tables.TryGetValue(file, out int tableId)
///     &amp;&amp; ResolveTable(tableId) is { } table
///     &amp;&amp; TryFindRow(file, table, name, out int rowIndex);
///
/// Container container = preserveOriginal
///     ? original
///     : Container.Create(message, original.Version, destination.GameVersion);
/// </code>
/// <para>
/// Operators of one kind written in a row are one sequence, and a different operator or a <c>?:</c> inside an operand
/// is laid out on its own, from the line its operand starts on. The width is measured from where the expression starts
/// to where it ends, so what follows it on the line never decides. An expression in a call's argument stays on one
/// line whole, however long it is and with everything inside it, since the call wraps its arguments to give it room;
/// it splits there only when something inside it cannot be written on one line. An expression in a condition, which
/// NETAGENTS0019 keeps on one line, in an interpolation, in an attribute, in a list NETAGENTS0020 writes on one line,
/// or beside a comment keeps its layout.
/// </para>
/// </remarks>
internal static class OperatorLayout
{
    public const string RuleIdentifier = "NETAGENTS0055";

    // The operators of the expression that ends at this node, in the order written, or nothing when the node is no
    // such expression or only continues one.
    public static List<SyntaxToken>? GetOperators(SyntaxNode node)
    {
        if (node is ConditionalExpressionSyntax conditional)
            return [conditional.QuestionToken, conditional.ColonToken];

        if (node is not BinaryExpressionSyntax top || !IsSequence(top) || Continues(top))
            return null;

        // && and || group to the left and ?? to the right, so the sequence runs down that side.
        List<SyntaxToken> operators = [];
        ExpressionSyntax current = top;

        while (current is BinaryExpressionSyntax link && link.IsKind(top.Kind()))
        {
            operators.Add(link.OperatorToken);

            current = top.IsKind(SyntaxKind.CoalesceExpression) ? link.Right : link.Left;
        }

        operators.Sort(static (left, right) => left.SpanStart.CompareTo(right.SpanStart));

        return operators;
    }

    // The layout the expression must take, or nothing where the rule leaves it as written.
    public static bool? GetRequiredSplit(ExpressionSyntax node, List<SyntaxToken> operators, SemanticModel model, SourceText text, CancellationToken cancellationToken)
    {
        if (ChainLayout.IsHeldOnOneLine(node, model, text, cancellationToken))
            return null;

        // A comment beside an operator belongs to its author, who decides which line it goes on.
        foreach (SyntaxToken token in operators)
        {
            bool plain = IsWhitespace(token.GetPreviousToken().TrailingTrivia) && IsWhitespace(token.LeadingTrivia);

            if (!plain || !IsWhitespace(token.TrailingTrivia) || !IsWhitespace(token.GetNextToken().LeadingTrivia))
                return null;
        }

        // Inside a call's argument the expression stays whole, and the call wraps its arguments instead.
        return !StaysWhole(node) && !Fits(node, text);
    }

    public static bool IsSettled(ExpressionSyntax node, List<SyntaxToken> operators, bool split, SourceText text)
    {
        if (!split && StaysWhole(node))
        {
            return text.Lines.GetLineFromPosition(node.SpanStart).LineNumber == text.Lines
                .GetLineFromPosition(node.Span.End)
                .LineNumber;
        }

        string deeper = GetOperandIndentation(node, text);

        foreach (SyntaxToken token in operators)
        {
            TextLine line = text.Lines.GetLineFromPosition(token.SpanStart);
            bool starts = text.Lines.GetLineFromPosition(token.GetPreviousToken().Span.End).LineNumber != line.LineNumber;

            // The operator leads its operand's line, so nothing breaks between the two.
            if (starts != split || text.Lines.GetLineFromPosition(token.GetNextToken().SpanStart).LineNumber != line.LineNumber)
                return false;

            if (split && text.ToString(TextSpan.FromBounds(line.Start, token.SpanStart)) != deeper)
                return false;
        }

        return true;
    }

    // An expression written in a call's argument that can stand on one line: it stays there whole, however long, with
    // everything inside it, so nothing inside an argument breaks before the call around it wraps.
    public static bool IsWhole(SyntaxNode node)
    {
        return node is ExpressionSyntax expression && GetOperators(expression) is not null && StaysWhole(expression);
    }

    // Only the white space around the operators changes: a line break and one level of indentation in front of each
    // when the expression splits, and one space on each side when it does not. An expression that stays whole loses
    // every line break inside it.
    public static SyntaxNode Rewrite(SyntaxNode root, ExpressionSyntax node, List<SyntaxToken> operators, bool split, SourceText text)
    {
        if (!split && StaysWhole(node))
            return Join(root, node);

        Dictionary<SyntaxToken, SyntaxTriviaList> leading = [];
        Dictionary<SyntaxToken, SyntaxTriviaList> trailing = [];
        SyntaxTriviaList space = SyntaxFactory.TriviaList(SyntaxFactory.Space);
        SyntaxTriviaList lineBreak = SyntaxFactory.TriviaList(GetLineBreak(root));

        // An operand of a single token sits between two operators, so each side of a token is decided on its own.
        SyntaxTriviaList deeper = SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(GetOperandIndentation(node, text)));

        foreach (SyntaxToken token in operators)
        {
            trailing[token.GetPreviousToken()] = split ? lineBreak : space;
            leading[token] = split ? deeper : SyntaxTriviaList.Empty;
            trailing[token] = space;
            leading[token.GetNextToken()] = SyntaxTriviaList.Empty;
        }

        HashSet<SyntaxToken> changed = [.. leading.Keys, .. trailing.Keys];

        return root.ReplaceTokens(changed, (original, rewritten) => Respace(original, leading, trailing));
    }

    // Whether the expression can be written on one line: nothing inside it has to spread over lines, and no comment
    // inside it needs a line of its own.
    private static bool CanJoin(ExpressionSyntax node)
    {
        foreach (SyntaxNode inner in node.DescendantNodes())
        {
            if (inner is BlockSyntax or SwitchExpressionSyntax || CollectionLayout.AlwaysExpands(inner))
                return false;

            if ((ArgumentLayout.IsList(inner) || CollectionLayout.CanLayOut(inner)) && ArgumentLayout.RequiresMultipleLines(inner))
                return false;
        }

        foreach (SyntaxToken token in node.DescendantTokens())
        {
            if (token.Text.IndexOf(value: '\n') >= 0)
                return false;
        }

        foreach (SyntaxTrivia trivia in node.DescendantTrivia())
        {
            bool blank = trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia);

            if (!blank && node.Span.Contains(trivia.Span))
                return false;
        }

        return true;
    }

    // A longer sequence of the same operator continues through this expression, on the side that operator groups to.
    private static bool Continues(BinaryExpressionSyntax node)
    {
        return node.Parent is BinaryExpressionSyntax parent
            && parent.IsKind(node.Kind())
            && (node.IsKind(SyntaxKind.CoalesceExpression) ? parent.Right == node : parent.Left == node);
    }

    // The expression on one line, one space wherever its tokens had white space between them, measured from its column.
    // What follows it on the line is left out, since another expression there would make each decide the other's layout.
    private static bool Fits(ExpressionSyntax node, SourceText text)
    {
        foreach (SyntaxNode inner in node.DescendantNodes())
        {
            if (inner is BlockSyntax or SwitchExpressionSyntax || CollectionLayout.AlwaysExpands(inner))
                return false;
        }

        int width = node.SpanStart - text.Lines.GetLineFromPosition(node.SpanStart).Start;
        SyntaxToken last = node.GetLastToken();

        foreach (SyntaxToken token in node.DescendantTokens())
        {
            if (token.Text.IndexOf(value: '\n') >= 0)
                return false;

            width += token.Text.Length;

            if (token != last && (token.HasTrailingTrivia || token.GetNextToken().HasLeadingTrivia))
                width++;
        }

        return width <= LayoutLimits.MaximumInlineLength;
    }

    private static SyntaxTrivia GetLineBreak(SyntaxNode root)
    {
        foreach (SyntaxTrivia trivia in root.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                return trivia;
        }

        return SyntaxFactory.LineFeed;
    }

    // An operand on a line of its own goes one level deeper than the line the expression starts on.
    private static string GetOperandIndentation(ExpressionSyntax node, SourceText text)
    {
        TextLine start = text.Lines.GetLineFromPosition(node.SpanStart);
        int length = 0;

        while (start.Start + length < start.End && text[start.Start + length] is ' ' or '\t')
            length++;

        string indentation = text.ToString(new TextSpan(start.Start, length));

        return indentation + (indentation.IndexOf(value: '\t') >= 0 ? "\t" : "    ");
    }

    // The expression is written in a call's argument, with no statement of its own in between and no initializer
    // that gives each of its values a line of its own.
    private static bool IsArgument(ExpressionSyntax node)
    {
        foreach (SyntaxNode ancestor in node.Ancestors())
        {
            if (ancestor is StatementSyntax or MemberDeclarationSyntax || CollectionLayout.AlwaysExpands(ancestor))
                return false;

            if (ancestor is ArgumentSyntax)
                return true;
        }

        return false;
    }

    private static bool IsOnOneLine(SyntaxTriviaList trivia)
    {
        foreach (SyntaxTrivia piece in trivia)
        {
            if (piece.IsKind(SyntaxKind.EndOfLineTrivia))
                return false;
        }

        return true;
    }

    private static bool IsSequence(BinaryExpressionSyntax node)
    {
        return node.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.CoalesceExpression;
    }

    private static bool IsWhitespace(SyntaxTriviaList trivia)
    {
        foreach (SyntaxTrivia piece in trivia)
        {
            if (!piece.IsKind(SyntaxKind.WhitespaceTrivia) && !piece.IsKind(SyntaxKind.EndOfLineTrivia))
                return false;
        }

        return true;
    }

    // Every line break inside the expression goes; the formatter then spaces the tokens it brought together.
    private static SyntaxNode Join(SyntaxNode root, ExpressionSyntax node)
    {
        Dictionary<SyntaxToken, SyntaxTriviaList> leading = [];
        Dictionary<SyntaxToken, SyntaxTriviaList> trailing = [];
        SyntaxToken first = node.GetFirstToken();

        foreach (SyntaxToken token in node.DescendantTokens())
        {
            SyntaxToken previous = token.GetPreviousToken();

            if (token == first || (IsOnOneLine(previous.TrailingTrivia) && IsOnOneLine(token.LeadingTrivia)))
                continue;

            trailing[previous] = Separates(previous, token)
                ? SyntaxFactory.TriviaList(SyntaxFactory.Space)
                : SyntaxTriviaList.Empty;

            leading[token] = SyntaxTriviaList.Empty;
        }

        HashSet<SyntaxToken> changed = [.. leading.Keys, .. trailing.Keys];

        return root.ReplaceTokens(changed, (original, rewritten) => Respace(original, leading, trailing));
    }

    private static SyntaxToken Respace(SyntaxToken token, Dictionary<SyntaxToken, SyntaxTriviaList> leading, Dictionary<SyntaxToken, SyntaxTriviaList> trailing)
    {
        SyntaxToken respaced = leading.TryGetValue(token, out SyntaxTriviaList before) ? token.WithLeadingTrivia(before) : token;

        return trailing.TryGetValue(token, out SyntaxTriviaList after) ? respaced.WithTrailingTrivia(after) : respaced;
    }

    // Two tokens brought onto one line take a space between them, except beside a dot, after an opening bracket, and
    // before a closing one or a comma.
    private static bool Separates(SyntaxToken previous, SyntaxToken token)
    {
        bool opens = previous.Kind() is SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.DotToken;

        bool closes = token.Kind() is SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken or SyntaxKind.DotToken
            or SyntaxKind.CommaToken;

        return !opens && !closes;
    }

    // The expression is in a call's argument and nothing inside it has to spread over lines.
    private static bool StaysWhole(ExpressionSyntax node)
    {
        return IsArgument(node) && CanJoin(node);
    }
}
