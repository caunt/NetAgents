using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Finds string literals and interpolated strings joined with <c>+</c> on one line, which one string writes directly.
/// </summary>
/// <remarks>
/// <para>
/// <c>$"{name}{Environment.NewLine}" + $"Usage: {usage}"</c> becomes <c>$"{name}{Environment.NewLine}Usage: {usage}"</c>,
/// and <c>"first " + "second"</c> becomes <c>"first second"</c>. Only plain <c>"…"</c> and <c>$"…"</c> strings merge;
/// verbatim, raw, and UTF-8 strings are left alone, and so are pieces split over lines, which their author laid out on
/// purpose. A merge is made only when the merged text reads back as the same pieces of text and the same holes, so an
/// escape such as <c>\x1</c> never runs into the text after it.
/// </para>
/// <para>
/// An argument may bind differently once it is one interpolated string, since a method can take an interpolated
/// string handler, so a merged interpolated argument must leave the call bound to the same method.
/// </para>
/// </remarks>
internal static class LiteralConcatenation
{
    public const string RuleIdentifier = "NETAGENTS0050";

    // The one string the run of pieces ending at this addition becomes, or nothing when it is no such run or the merge
    // could change what the code does.
    public static ExpressionSyntax? GetMerged(BinaryExpressionSyntax top, SemanticModel model, SourceText text, CancellationToken cancellationToken)
    {
        if (!top.IsKind(SyntaxKind.AddExpression) || ContinuesRun(top))
            return null;

        List<ExpressionSyntax>? pieces = GetPieces(top);

        if (pieces is null || !IsOnOneLine(top, text))
            return null;

        bool interpolated = pieces.Exists(static piece => piece is InterpolatedStringExpressionSyntax);
        ExpressionSyntax? merged = interpolated ? MergeInterpolated(pieces) : MergeLiterals(pieces);

        return merged is null || (interpolated && !KeepsBinding(top, merged, model, cancellationToken)) ? null : merged.WithTriviaFrom(top);
    }

    // A longer run continues through the addition this one is the left side of.
    private static bool ContinuesRun(BinaryExpressionSyntax top)
    {
        return top.Parent is BinaryExpressionSyntax parent && parent.IsKind(SyntaxKind.AddExpression) && parent.Left == top && IsPiece(parent.Right);
    }

    // The pieces from left to right, when every operand of the run is one.
    private static List<ExpressionSyntax>? GetPieces(BinaryExpressionSyntax top)
    {
        List<ExpressionSyntax> pieces = [];
        ExpressionSyntax node = top;

        while (node is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            if (!IsPiece(binary.Right))
                return null;

            pieces.Add(binary.Right);

            node = binary.Left;
        }

        if (!IsPiece(node))
            return null;

        pieces.Add(node);
        pieces.Reverse();

        return pieces;
    }

    // The holes and texts a string is made of, with neighbouring texts joined, which a merge must reproduce exactly.
    private static List<string> GetShape(IEnumerable<ExpressionSyntax> pieces)
    {
        List<string> shape = [];
        string text = string.Empty;

        foreach (ExpressionSyntax piece in pieces)
        {
            if (piece is LiteralExpressionSyntax literal)
            {
                text += literal.Token.ValueText;

                continue;
            }

            foreach (InterpolatedStringContentSyntax content in ((InterpolatedStringExpressionSyntax)piece).Contents)
            {
                if (content is InterpolatedStringTextSyntax part)
                {
                    // The token's value keeps a brace escape doubled, though it stands for one brace.
                    text += part.TextToken
                        .ValueText
                        .Replace(oldValue: "{{", newValue: "{")
                        .Replace(oldValue: "}}", newValue: "}");

                    continue;
                }

                // An empty text between two holes still separates them, so it is recorded like any other text.
                shape.Add("T" + text);
                shape.Add("H" + content.ToString());

                text = string.Empty;
            }
        }

        shape.Add("T" + text);

        return shape;
    }

    // The text between a string's quotes, as written.
    private static string GetWrittenContents(ExpressionSyntax piece)
    {
        if (piece is LiteralExpressionSyntax literal)
            return literal.Token.Text.Substring(startIndex: 1, literal.Token.Text.Length - 2);

        // A plain interpolated string opens with $" and closes with ".
        string written = piece.ToString();

        return written.Substring(startIndex: 2, written.Length - 3);
    }

    private static bool IsOnOneLine(BinaryExpressionSyntax top, SourceText text)
    {
        if (text.Lines.GetLineFromPosition(top.SpanStart).LineNumber != text.Lines.GetLineFromPosition(top.Span.End).LineNumber)
            return false;

        // A comment between the pieces is the author's, and a merge would have nowhere to put it.
        foreach (SyntaxTrivia trivia in top.DescendantTrivia())
        {
            if (top.Span.Contains(trivia.Span) && !trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                return false;
        }

        return true;
    }

    // A plain string literal or a plain interpolated string; verbatim, raw, and UTF-8 strings escape differently.
    private static bool IsPiece(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression)
                && literal.Token.IsKind(SyntaxKind.StringLiteralToken) && literal.Token
                    .Text
                    .StartsWith(value: "\"", StringComparison.Ordinal),
            InterpolatedStringExpressionSyntax interpolated => interpolated.StringStartToken
                .IsKind(SyntaxKind.InterpolatedStringStartToken),
            _ => false,
        };
    }

    // An interpolated argument could bind to a method taking an interpolated string handler, so the merged call must
    // still bind to the method the addition did. Only calls and constructions written out with their type are checked;
    // an argument anywhere else is left alone.
    private static bool KeepsBinding(BinaryExpressionSyntax top, ExpressionSyntax merged, SemanticModel model, CancellationToken cancellationToken)
    {
        SyntaxNode? parent = top.Parent;

        while (parent is ParenthesizedExpressionSyntax)
            parent = parent.Parent;

        if (parent is AttributeArgumentSyntax)
            return false;

        if (parent is not ArgumentSyntax argument)
            return true;

        // A target-typed new or an element access cannot be bound on its own, so only these two calls are checked.
        SyntaxNode? holder = argument.Parent?.Parent;

        if (holder is not ExpressionSyntax call || call is not (InvocationExpressionSyntax or ObjectCreationExpressionSyntax))
            return false;

        ISymbol? before = model.GetSymbolInfo(call, cancellationToken).Symbol;
        ExpressionSyntax rewritten = call.ReplaceNode(top, merged);

        ISymbol? after = model.GetSpeculativeSymbolInfo(call.SpanStart, rewritten, SpeculativeBindingOption.BindAsExpression)
            .Symbol;

        return before is not null && SymbolEqualityComparer.Default.Equals(before, after);
    }

    private static ExpressionSyntax? MergeInterpolated(List<ExpressionSyntax> pieces)
    {
        // A literal moving into an interpolated string doubles its braces, which would otherwise open holes.
        IEnumerable<string> contents = pieces.Select(static piece => piece is LiteralExpressionSyntax
            ? GetWrittenContents(piece).Replace(oldValue: "{", newValue: "{{").Replace(oldValue: "}", newValue: "}}")
            : GetWrittenContents(piece));

        ExpressionSyntax merged = SyntaxFactory.ParseExpression("$\"" + string.Concat(contents) + "\"");

        return merged is not InterpolatedStringExpressionSyntax || merged.ContainsDiagnostics
            ? null
            : GetShape(pieces).SequenceEqual(GetShape(pieces: [merged]), StringComparer.Ordinal) ? merged : null;
    }

    private static ExpressionSyntax? MergeLiterals(List<ExpressionSyntax> pieces)
    {
        ExpressionSyntax merged = SyntaxFactory.ParseExpression("\"" + string.Concat(pieces.Select(GetWrittenContents)) + "\"");
        string value = string.Concat(pieces.Select(static piece => ((LiteralExpressionSyntax)piece).Token.ValueText));

        return merged is not LiteralExpressionSyntax literal || merged.ContainsDiagnostics
            ? null
            : string.Equals(literal.Token.ValueText, value, StringComparison.Ordinal) ? merged : null;
    }
}
