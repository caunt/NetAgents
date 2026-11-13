using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting.Collections;

/// <summary>
/// Lays out collection expressions and initializers of two or more elements: on one line when their contents fit within
/// 128 characters, otherwise one element per line.
/// </summary>
/// <remarks>
/// <para>
/// A collection expression that does not fit opens its bracket on a line of its own, puts every element on a line of
/// its own one level deeper with a comma after each, the last included, and closes its bracket on a line of its own:
/// </para>
/// <code>
/// string[] lines =
/// [
///     "Download IPAs and extract or update Supercell server public keys.",
///     string.Empty,
///     "Usage:",
/// ];
/// </code>
/// <para>
/// The bracket opens on a line of its own where the collection is a value written after <c>=</c>, <c>=&gt;</c>,
/// <c>return</c>, or a parameter name; as a positional argument or an element, it stays where the list holding it puts
/// it. An initializer that does not fit puts every element on a line of its own and its closing brace after them, and
/// the formatter gives the opening brace a line of its own, as it does for every multiline initializer.
/// </para>
/// <para>
/// A collection that fits joins the token before it where it is such a value, or the initializer of a creation, and
/// drops a trailing comma. A collection holding a comment or a directive keeps the layout its author gave it.
/// </para>
/// </remarks>
internal static class CollectionLayout
{
    public static bool CanLayOut(SyntaxNode node)
    {
        if (node is not (CollectionExpressionSyntax { Elements.Count: > 1 } or InitializerExpressionSyntax { Expressions.Count: > 1 }))
            return false;

        if (node.ContainsDirectives || node.ContainsDiagnostics)
            return false;

        foreach (SyntaxTrivia trivia in node.DescendantTrivia())
        {
            if (node.Span.Contains(trivia.Span) && !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                return false;
        }

        return true;
    }

    public static TextChange[] GetChanges(SyntaxNode collection, SourceText source)
    {
        SyntaxNode[] items = [.. collection.ChildNodes()];
        SyntaxToken opening = collection.GetFirstToken();
        SyntaxToken closing = collection.GetLastToken();

        // A value after =, =>, return, or a name joins the token before it on one line, and opens below it otherwise.
        SyntaxToken previous = opening.GetPreviousToken();
        bool value = IsValue(collection, source);

        // The contents on one line, without the trailing comma a multiline layout leaves.
        if (!ArgumentLayout.RequiresMultipleLines(collection))
        {
            string compact = WithoutTrailingSeparator(collection).WithoutTrivia()
                .NormalizeWhitespace(indentation: "", eol: " ")
                .ToFullString();

            return [new TextChange(TextSpan.FromBounds(value ? previous.Span.End : opening.SpanStart, closing.Span.End), (value ? " " : "") + compact)];
        }

        // New lines reuse the file's line break, and elements sit one level deeper than the delimiters around them.
        TextLine firstLine = source.Lines[index: 0];
        string newline = firstLine.EndIncludingLineBreak > firstLine.End ? source.ToString(TextSpan.FromBounds(firstLine.End, firstLine.EndIncludingLineBreak)) : "\n";
        bool opensBelow = value && collection is CollectionExpressionSyntax;
        string indentation = ArgumentLayout.GetIndentation(source, opensBelow ? previous.SpanStart : opening.SpanStart);

        // The line breaks around the delimiters and between the elements, in the order they appear.
        string itemIndentation = indentation + "    ";
        List<TextChange> changes = [];

        if (opensBelow)
            changes.Add(new TextChange(TextSpan.FromBounds(previous.Span.End, opening.SpanStart), newline + indentation));

        changes.Add(ArgumentLayout.SeparateItems(source, opening.Span.End, items[0].SpanStart, newline, itemIndentation));

        for (int index = 1; index < items.Length; index++)
        {
            int separator = items[index].GetFirstToken().GetPreviousToken().Span.End;

            changes.Add(ArgumentLayout.SeparateItems(source, separator, items[index].SpanStart, newline, itemIndentation));
        }

        changes.Add(Close(collection, source, newline, indentation));

        return [.. changes.OrderBy(static change => change.Span.Start)];
    }

    public static bool HasExpectedLayout(SyntaxNode collection, SourceText source)
    {
        SyntaxNode[] items = [.. collection.ChildNodes()];
        SyntaxToken opening = collection.GetFirstToken();
        SyntaxToken closing = collection.GetLastToken();

        // A one-line collection has no comma after its last element; a multiline collection expression has one.
        SyntaxNode last = items[items.Length - 1];
        bool trailing = last.GetLastToken().GetNextToken().IsKind(SyntaxKind.CommaToken);

        if (!ArgumentLayout.RequiresMultipleLines(collection))
        {
            int start = IsValue(collection, source) ? opening.GetPreviousToken().Span.End : opening.SpanStart;

            return !trailing && !ArgumentLayout.Breaks(source, start, closing.SpanStart);
        }

        for (int index = 1; index < items.Length; index++)
        {
            if (!ArgumentLayout.Breaks(source, items[index - 1].Span.End, items[index].SpanStart))
                return false;
        }

        // The first element starts a line below the opening delimiter, and the closing delimiter a line below the last.
        bool enclosed = ArgumentLayout.Breaks(source, opening.Span.End, items[0].SpanStart) && ArgumentLayout.Breaks(source, last.Span.End, closing.SpanStart);

        // A collection expression written as a value opens below the token before it, and ends with a comma.
        bool initializer = collection is InitializerExpressionSyntax;
        int previousEnd = opening.GetPreviousToken().Span.End;
        bool opensBelow = initializer || !IsValue(collection, source) || ArgumentLayout.Breaks(source, previousEnd, opening.SpanStart);

        return enclosed && opensBelow && (initializer || trailing);
    }

    // The closing delimiter starts a line of its own; a collection expression gets a comma after its last element first,
    // while an initializer keeps whatever its author wrote there.
    private static TextChange Close(SyntaxNode collection, SourceText source, string newline, string indentation)
    {
        SyntaxToken closing = collection.GetLastToken();
        SyntaxToken previous = closing.GetPreviousToken();

        return collection is InitializerExpressionSyntax || previous.IsKind(SyntaxKind.CommaToken)
            ? ArgumentLayout.SeparateItems(source, previous.Span.End, closing.SpanStart, newline, indentation)
            : new TextChange(TextSpan.FromBounds(previous.Span.End, closing.SpanStart), "," + newline + indentation);
    }

    // A value written after =, =>, return, or a parameter name, or the initializer of a creation, which joins the token
    // before it when it fits on one line; as an element or a positional argument, the list holding it decides where it
    // starts.
    private static bool IsValue(SyntaxNode collection, SourceText source)
    {
        SyntaxToken opening = collection.GetFirstToken();

        bool value = collection.Parent switch
        {
            EqualsValueClauseSyntax or ArrowExpressionClauseSyntax or ReturnStatementSyntax or YieldStatementSyntax => true,
            BaseObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or WithExpressionSyntax => true,
            StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax => true,
            AssignmentExpressionSyntax assignment => assignment.Right == collection,
            ArgumentSyntax argument => argument.NameColon is not null,
            LambdaExpressionSyntax lambda => lambda.ExpressionBody == collection,
            _ => false,
        };

        return value && !ArgumentLayout.HasComment(source, opening.GetPreviousToken().Span.End, opening.SpanStart);
    }

    private static List<SyntaxToken> TakeLeading(IEnumerable<SyntaxToken> separators, int count)
    {
        List<SyntaxToken> kept = [];

        foreach (SyntaxToken separator in separators)
        {
            if (kept.Count == count)
                break;

            kept.Add(separator);
        }

        return kept;
    }

    private static SyntaxNode WithoutTrailingSeparator(SyntaxNode collection)
    {
        switch (collection)
        {
            case CollectionExpressionSyntax expression when expression.Elements.SeparatorCount == expression.Elements.Count:
                {
                    SeparatedSyntaxList<CollectionElementSyntax> elements = expression.Elements;
                    List<CollectionElementSyntax> nodes = [.. elements];

                    return expression.WithElements(SyntaxFactory.SeparatedList(nodes, TakeLeading(elements.GetSeparators(), elements.Count - 1)));
                }
            case InitializerExpressionSyntax initializer when initializer.Expressions.SeparatorCount == initializer.Expressions.Count:
                {
                    SeparatedSyntaxList<ExpressionSyntax> expressions = initializer.Expressions;
                    List<ExpressionSyntax> nodes = [.. expressions];

                    return initializer.WithExpressions(SyntaxFactory.SeparatedList(nodes, TakeLeading(expressions.GetSeparators(), expressions.Count - 1)));
                }
            default:
                return collection;
        }
    }
}
