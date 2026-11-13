using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting.Chains;

/// <summary>
/// Lays out member chains, such as <c>rows.Values.Select(…).OrderBy(…)</c>, the one way the rules accept.
/// </summary>
/// <remarks>
/// <para>
/// A chain's links are the property, field, and event accesses and the calls reached through dots after its
/// receiver; namespace and type qualifiers belong to the receiver, and an element access belongs to the link before
/// it. A chain of up to four links stays on one line when that line fits within 128 characters. A longer chain, or
/// one that does not fit, puts each call on a line of its own, the first call included, one level deeper than the
/// line it starts on, while property and field accesses stay on the line of the receiver or call they follow:
/// </para>
/// <code>
/// string[][] rows = [.. rowsByVersion.Values
///     .Select(static value => value.Cells)
///     .OrderByDescending(cells => cells[section.VersionColumnIndex], AppVersion.ValueComparer)];
/// </code>
/// <para>
/// A chain with fewer than two calls has nothing to align and keeps its layout, and so does a chain in a condition,
/// which NETAGENTS0019 keeps on one line, in an argument or parameter list that NETAGENTS0020 writes on one line, in
/// an interpolation, beside a comment, or reached through <c>?.</c>.
/// </para>
/// </remarks>
internal static class ChainLayout
{
    public const int MaximumLinks = 4;

    public const string RuleIdentifier = "NETAGENTS0047";

    // The links of the chain that ends at this expression, or nothing when it is no chain the rule lays out. Leading
    // namespace and type qualifiers are bound only when they could change the count.
    public static List<ChainLink>? GetLinks(ExpressionSyntax top, SemanticModel model, CancellationToken cancellationToken)
    {
        List<ChainLink> links = [];
        ExpressionSyntax node = top;

        while (node is InvocationExpressionSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax)
        {
            switch (node)
            {
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax called } when called.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                    {
                        links.Add(new ChainLink(called, call: true));

                        node = called.Expression;

                        break;
                    }
                case InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax }:
                    return null;
                case InvocationExpressionSyntax:
                    return Finish(links, model, cancellationToken);
                case MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                    {
                        links.Add(new ChainLink(access, call: false));

                        node = access.Expression;

                        break;
                    }
                case ElementAccessExpressionSyntax element:
                    {
                        node = element.Expression;

                        break;
                    }
                default:
                    return null;
            }
        }

        return node is ConditionalAccessExpressionSyntax ? null : Finish(links, model, cancellationToken);
    }

    // The layout the chain must take, or nothing where the rule leaves it as written.
    public static bool? GetRequiredSplit(ExpressionSyntax top, List<ChainLink> links, SourceText text)
    {
        return links.Count(static link => link.IsCall) < 2 || IsExempt(top, links)
            ? null
            : links.Count > MaximumLinks || !Fits(top, links, text);
    }

    public static bool IsSettled(List<ChainLink> links, bool split, SourceText text)
    {
        foreach (ChainLink link in links)
        {
            if (StartsLine(link.Dot, text) != (split && link.IsCall))
                return false;
        }

        return true;
    }

    // A chain ends at a call, member access, or element access that nothing continues: no member, call, or element
    // access is applied to it.
    public static bool IsTop(ExpressionSyntax node)
    {
        return node is InvocationExpressionSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax && node.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Expression != node,
            InvocationExpressionSyntax invocation => invocation.Expression != node,
            ElementAccessExpressionSyntax element => element.Expression != node,
            ConditionalAccessExpressionSyntax conditional => conditional.Expression != node,
            _ => true,
        };
    }

    // Whether a statement holds a chain this rule splits although the statement fits on one line, which
    // NETAGENTS0045 must then leave alone.
    public static bool RequiresSplit(SyntaxNode node, SemanticModel model, SourceText text, CancellationToken cancellationToken)
    {
        foreach (ExpressionSyntax expression in node.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            if (!IsTop(expression))
                continue;

            List<ChainLink>? links = GetLinks(expression, model, cancellationToken);

            if (links is not null && GetRequiredSplit(expression, links, text) == true)
                return true;
        }

        return false;
    }

    // Line breaks before dots go or come; everything else in the chain is left to the formatter, which indents the new
    // lines as it indents any continuation.
    public static SyntaxNode Rewrite(SyntaxNode root, List<ChainLink> links, bool split, SourceText text)
    {
        SyntaxTriviaList lineBreak = SyntaxFactory.TriviaList(GetLineBreak(root));
        Dictionary<SyntaxToken, SyntaxToken> replacements = [];

        // A call moved to a line of its own goes one level deeper than the line the chain starts on.
        TextLine start = text.Lines.GetLineFromPosition(links[index: 0].Access.SpanStart);
        string indentation = text.ToString(TextSpan.FromBounds(start.Start, start.Start + CountIndentation(text, start)));
        string deeper = indentation + (indentation.IndexOf(value: '\t') >= 0 ? "\t" : "    ");

        foreach (ChainLink link in links)
        {
            bool breaks = split && link.IsCall;
            SyntaxToken previous = link.Dot.GetPreviousToken();

            replacements[previous] = previous.WithTrailingTrivia(breaks ? lineBreak : SyntaxTriviaList.Empty);
            replacements[link.Dot] = link.Dot.WithLeadingTrivia(breaks ? SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(deeper)) : SyntaxTriviaList.Empty);
        }

        return root.ReplaceTokens(replacements.Keys, (original, rewritten) => replacements[original]);
    }

    private static int CountIndentation(SourceText text, TextLine line)
    {
        int count = 0;

        while (line.Start + count < line.End && text[line.Start + count] is ' ' or '\t')
            count++;

        return count;
    }

    // The leading accesses that name a namespace or a type are part of the receiver, as Console is in
    // Console.Error.WriteLine(…).
    private static List<ChainLink> Finish(List<ChainLink> links, SemanticModel model, CancellationToken cancellationToken)
    {
        links.Reverse();

        if (links.Count <= MaximumLinks)
            return links;

        int qualifiers = 0;

        while (qualifiers < links.Count && IsQualifier(links[qualifiers], model, cancellationToken))
            qualifiers++;

        return qualifiers == 0 ? links : links.GetRange(qualifiers, links.Count - qualifiers);
    }

    // The chain on one line, with no space around a dot and one wherever the tokens had whitespace between them,
    // measured from its column to the end of what follows it on its last line.
    private static bool Fits(ExpressionSyntax top, List<ChainLink> links, SourceText text)
    {
        foreach (SyntaxNode node in top.DescendantNodes())
        {
            if (node is BlockSyntax or SwitchExpressionSyntax)
                return false;
        }

        TextLine start = text.Lines.GetLineFromPosition(top.SpanStart);
        TextLine end = text.Lines.GetLineFromPosition(top.Span.End);
        int width = top.SpanStart - start.Start + text.ToString(TextSpan.FromBounds(top.Span.End, end.End)).TrimEnd().Length;

        // A dot joins its neighbours with no space, and every other gap that holds whitespace keeps one.
        HashSet<SyntaxToken> dots = [.. links.Select(static link => link.Dot)];
        SyntaxToken last = top.GetLastToken();

        foreach (SyntaxToken token in top.DescendantTokens())
        {
            if (token.Text.IndexOf(value: '\n') >= 0)
                return false;

            width += token.Text.Length;

            SyntaxToken next = token.GetNextToken();
            bool spaced = token.HasTrailingTrivia || next.HasLeadingTrivia;

            if (token != last && spaced && !dots.Contains(token) && !dots.Contains(next))
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

    private static bool IsCondition(SyntaxNode node)
    {
        return node.Parent switch
        {
            IfStatementSyntax statement => statement.Condition == node,
            WhileStatementSyntax statement => statement.Condition == node,
            DoStatementSyntax statement => statement.Condition == node,
            ForStatementSyntax statement => statement.Condition == node,
            ConditionalExpressionSyntax expression => expression.Condition == node,
            CatchFilterClauseSyntax filter => filter.FilterExpression == node,
            WhenClauseSyntax clause => clause.Condition == node,
            _ => false,
        };
    }

    // NETAGENTS0019 keeps conditions on one line, an interpolation holds no line break, NETAGENTS0020 writes a list
    // whose contents fit on one line, and a comment beside a dot belongs to its author, so the rule lays none of those
    // out.
    private static bool IsExempt(ExpressionSyntax top, List<ChainLink> links)
    {
        // The search for conditions stops at the statement holding the chain, since one beyond it is another
        // statement's. A list joins everything inside it, a lambda's statements included, so that search goes on.
        bool inStatement = true;

        foreach (SyntaxNode ancestor in top.AncestorsAndSelf())
        {
            if (inStatement && (IsCondition(ancestor) || ancestor is InterpolationSyntax or AttributeArgumentSyntax))
                return true;

            if (IsJoinedList(ancestor))
                return true;

            inStatement = inStatement && ancestor is not StatementSyntax;
        }

        foreach (ChainLink link in links)
        {
            if (!IsWhitespace(link.Dot.LeadingTrivia) || !IsWhitespace(link.Dot.GetPreviousToken().TrailingTrivia))
                return true;
        }

        return false;
    }

    // An argument or parameter list NETAGENTS0020 writes on one line, with every chain inside it joined.
    private static bool IsJoinedList(SyntaxNode node)
    {
        return ArgumentLayout.IsList(node) && !node.ContainsDiagnostics && !node.ContainsDirectives && !ArgumentLayout.RequiresMultipleLines(node);
    }

    // A namespace or type the chain is reached through, as Console is in Console.Error.WriteLine(…).
    private static bool IsQualifier(ChainLink link, SemanticModel model, CancellationToken cancellationToken)
    {
        return !link.IsCall && model.GetSymbolInfo(link.Access, cancellationToken).Symbol is INamespaceOrTypeSymbol;
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

    private static bool StartsLine(SyntaxToken dot, SourceText text)
    {
        return text.Lines.GetLineFromPosition(dot.GetPreviousToken().Span.End).LineNumber != text.Lines.GetLineFromPosition(dot.SpanStart).LineNumber;
    }
}
