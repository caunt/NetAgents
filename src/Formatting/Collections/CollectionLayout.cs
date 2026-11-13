using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting.Collections;

/// <summary>
/// Lays out collection expressions and initializers of two or more elements the way NETAGENTS0020 lays out argument
/// lists: on one line when their contents fit within 128 characters, otherwise one element per line.
/// </summary>
/// <remarks>
/// <para>
/// A collection expression that does not fit keeps its first element beside <c>[</c>, puts each later element on a line
/// of its own one level deeper, and closes right after the last element, without a trailing comma:
/// </para>
/// <code>
/// string[] lines = ["Download IPAs and extract or update Supercell server public keys.",
///     string.Empty,
///     "Usage:"];
/// </code>
/// <para>
/// An initializer that does not fit puts every element on a line of its own and its closing brace after them, and the
/// formatter gives the opening brace a line of its own, as it does for every multiline initializer. Where the
/// collection is a value written after <c>=</c>, <c>=&gt;</c>, <c>return</c>, or a parameter name, or the initializer
/// of a creation, its opening delimiter joins the token before it. A collection holding a comment or a directive keeps
/// the layout its author gave it.
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

        // Where the collection is a value after =, =>, return, or a name, its opening delimiter joins the token before.
        SyntaxToken previous = opening.GetPreviousToken();
        bool attaches = Attaches(collection, source);

        // The contents on one line, without the trailing comma a multiline layout may have left.
        if (!ArgumentLayout.RequiresMultipleLines(collection))
        {
            string compact = WithoutTrailingSeparator(collection).WithoutTrivia()
                .NormalizeWhitespace(indentation: "", eol: " ")
                .ToFullString();

            return [new TextChange(TextSpan.FromBounds(attaches ? previous.Span.End : opening.SpanStart, closing.Span.End), (attaches ? " " : "") + compact)];
        }

        // New lines reuse the file's line break, and elements sit one level deeper than the line the collection opens on.
        TextLine firstLine = source.Lines[index: 0];
        string newline = firstLine.EndIncludingLineBreak > firstLine.End ? source.ToString(TextSpan.FromBounds(firstLine.End, firstLine.EndIncludingLineBreak)) : "\n";
        string indentation = ArgumentLayout.GetIndentation(source, attaches ? previous.SpanStart : opening.SpanStart);
        string itemIndentation = indentation + "    ";

        // The line breaks before and after every element, in the order they appear.
        List<TextChange> changes = [];

        if (attaches)
            changes.Add(new TextChange(TextSpan.FromBounds(previous.Span.End, opening.SpanStart), newText: " "));

        // An initializer opens its elements on lines of their own; a collection expression keeps the first beside its
        // bracket, as an argument list does.
        bool initializer = collection is InitializerExpressionSyntax;

        changes.Add(initializer
            ? ArgumentLayout.SeparateItems(source, opening.Span.End, items[0].SpanStart, newline, itemIndentation)
            : new TextChange(TextSpan.FromBounds(opening.Span.End, items[0].SpanStart), string.Empty));

        for (int index = 1; index < items.Length; index++)
        {
            int separator = items[index].GetFirstToken().GetPreviousToken().Span.End;

            changes.Add(ArgumentLayout.SeparateItems(source, separator, items[index].SpanStart, newline, itemIndentation));
        }

        SyntaxNode last = items[items.Length - 1];

        changes.Add(initializer
            ? ArgumentLayout.SeparateItems(source, closing.GetPreviousToken().Span.End, closing.SpanStart, newline, indentation)
            : new TextChange(TextSpan.FromBounds(last.Span.End, closing.SpanStart), string.Empty));

        return [.. changes.OrderBy(static change => change.Span.Start)];
    }

    public static bool HasExpectedLayout(SyntaxNode collection, SourceText source)
    {
        SyntaxNode[] items = [.. collection.ChildNodes()];
        SyntaxToken opening = collection.GetFirstToken();
        SyntaxToken closing = collection.GetLastToken();

        // A comma after the last element is left over from a layout with the closing delimiter on a line of its own.
        SyntaxNode last = items[items.Length - 1];
        bool trailing = last.GetLastToken().GetNextToken().IsKind(SyntaxKind.CommaToken);

        if (!ArgumentLayout.RequiresMultipleLines(collection))
        {
            int start = Attaches(collection, source) ? opening.GetPreviousToken().Span.End : opening.SpanStart;

            return !trailing && !ArgumentLayout.Breaks(source, start, closing.SpanStart);
        }

        for (int index = 1; index < items.Length; index++)
        {
            if (!ArgumentLayout.Breaks(source, items[index - 1].Span.End, items[index].SpanStart))
                return false;
        }

        if (collection is InitializerExpressionSyntax)
            return ArgumentLayout.Breaks(source, opening.Span.End, items[0].SpanStart) && ArgumentLayout.Breaks(source, last.Span.End, closing.SpanStart);

        bool attached = !Attaches(collection, source) || !ArgumentLayout.Breaks(source, opening.GetPreviousToken().Span.End, opening.SpanStart);

        return attached && !trailing && !ArgumentLayout.Breaks(source, opening.Span.End, items[0].SpanStart) && !ArgumentLayout.Breaks(source, last.Span.End, closing.SpanStart);
    }

    // The opening delimiter joins the token before it where the collection is a value written after =, =>, return, or
    // a parameter name, or the initializer of a creation; as an element or a positional argument, the list holding it
    // decides where it starts.
    private static bool Attaches(SyntaxNode collection, SourceText source)
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

        // A multiline initializer's brace goes where the formatter puts it.
        return (collection is not InitializerExpressionSyntax || !ArgumentLayout.RequiresMultipleLines(collection)) && value && !ArgumentLayout.HasComment(source, opening.GetPreviousToken().Span.End, opening.SpanStart);
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
