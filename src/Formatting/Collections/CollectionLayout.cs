using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting.Collections;

/// <summary>
/// Lays out collection expressions and initializers: object-like initializers always one element per line, and
/// collections of two or more elements on one line when their contents fit within 128 characters, otherwise one
/// element per line.
/// </summary>
/// <remarks>
/// <para>
/// Object, indexer, and nested member initializers, <c>with</c> expressions, anonymous objects, and the rows of a
/// multidimensional array are braced blocks like any other: every element goes on a line of its own and the closing
/// brace below them, however short they are, and the opening brace goes on a line of its own (#55).
/// </para>
/// <code>
/// HttpClient client = new()
/// {
///     Timeout = TimeSpan.FromMinutes(minutes: 30),
/// };
/// </code>
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
/// it. Collection and array initializers that do not fit put every element on a line of its own like object
/// initializers. A collection that fits joins the token before it where it is such a value, or the initializer of a
/// creation, and drops a trailing comma; the <c>{ key, value }</c> pairs of a dictionary are such collections. A
/// collection holding a comment or a directive keeps the layout its author gave it.
/// </para>
/// </remarks>
internal static class CollectionLayout
{
    // Initializers that fill in an object take a line per element however short they are, as every other braced block
    // does: object, indexer, and nested member initializers, with expressions, anonymous objects, and the rows of a
    // multidimensional array (#55).
    public static bool AlwaysExpands(SyntaxNode node)
    {
        return node switch
        {
            InitializerExpressionSyntax initializer => initializer.Expressions.Count > 0 && IsObjectLike(initializer),
            AnonymousObjectCreationExpressionSyntax anonymous => anonymous.Initializers.Count > 0,
            _ => false,
        };
    }

    public static bool CanLayOut(SyntaxNode node)
    {
        bool counted = node is CollectionExpressionSyntax { Elements.Count: > 1 } or InitializerExpressionSyntax { Expressions.Count: > 1 };

        if (!counted && !AlwaysExpands(node))
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
        SyntaxToken opening = GetOpening(collection);
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
        bool opensBelow = IsBraced(collection) ? !ArgumentLayout.HasComment(source, previous.Span.End, opening.SpanStart) : value;

        // A delimiter moving below the token before it takes that line's indentation; one already on a line of its own,
        // such as a row its outer initializer placed, keeps the indentation it has.
        bool moves = opensBelow && !ArgumentLayout.Breaks(source, previous.Span.End, opening.SpanStart);
        string indentation = ArgumentLayout.GetIndentation(source, moves ? previous.SpanStart : opening.SpanStart);

        // The line breaks around the delimiters and between the elements, in the order they appear.
        string itemIndentation = indentation + "    ";
        List<TextChange> changes = [];

        if (moves)
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
        SyntaxToken opening = GetOpening(collection);
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

        // A brace, and a collection expression's bracket where it is a value, open below the token before them, unless a
        // comment stands between; a collection expression also ends with a comma.
        bool braced = IsBraced(collection);
        int previousEnd = opening.GetPreviousToken().Span.End;
        bool required = braced ? !ArgumentLayout.HasComment(source, previousEnd, opening.SpanStart) : IsValue(collection, source);
        bool opensBelow = !required || ArgumentLayout.Breaks(source, previousEnd, opening.SpanStart);

        return enclosed && opensBelow && (braced || trailing);
    }

    // The closing delimiter starts a line of its own; a collection expression gets a comma after its last element first,
    // while an initializer keeps whatever its author wrote there.
    private static TextChange Close(SyntaxNode collection, SourceText source, string newline, string indentation)
    {
        SyntaxToken closing = collection.GetLastToken();
        SyntaxToken previous = closing.GetPreviousToken();

        return IsBraced(collection) || previous.IsKind(SyntaxKind.CommaToken)
            ? ArgumentLayout.SeparateItems(source, previous.Span.End, closing.SpanStart, newline, indentation)
            : new TextChange(TextSpan.FromBounds(previous.Span.End, closing.SpanStart), "," + newline + indentation);
    }

    // An anonymous object opens at its brace, after the new keyword; every other collection opens at its first token.
    private static SyntaxToken GetOpening(SyntaxNode collection)
    {
        return collection is AnonymousObjectCreationExpressionSyntax anonymous ? anonymous.OpenBraceToken : collection.GetFirstToken();
    }

    // Initializers and anonymous objects open their brace on a line of its own and leave their trailing comma to the
    // author.
    private static bool IsBraced(SyntaxNode collection)
    {
        return collection is InitializerExpressionSyntax or AnonymousObjectCreationExpressionSyntax;
    }

    private static bool IsObjectLike(InitializerExpressionSyntax initializer)
    {
        return initializer.IsKind(SyntaxKind.ObjectInitializerExpression) || initializer.IsKind(SyntaxKind.WithInitializerExpression) || (initializer.IsKind(SyntaxKind.ArrayInitializerExpression) && initializer.Parent is InitializerExpressionSyntax row
            && row.IsKind(SyntaxKind.ArrayInitializerExpression));
    }

    // A value written after =, =>, return, or a parameter name, or the initializer of a creation, which joins the token
    // before it when it fits on one line; as an element or a positional argument, the list holding it decides where it
    // starts.
    private static bool IsValue(SyntaxNode collection, SourceText source)
    {
        SyntaxToken opening = GetOpening(collection);

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
