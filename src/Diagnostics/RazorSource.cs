using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Diagnostics;

// Razor hands the compiler the C# a component or view author wrote inside source-generated code. Each
// stretch it copies from a .razor or .cshtml file follows a #line directive naming that file, and the
// rendering code it writes itself between those stretches is hidden or names no file at all.
internal static class RazorSource
{
    private static readonly ConditionalWeakTable<SyntaxTree, StrongBox<ImmutableArray<TextSpan>>> AuthoredSpans = new();

    public static Action<OperationAnalysisContext> Confine(Action<OperationAnalysisContext> action)
    {
        return context =>
        {
            if (Admits(context.Operation.Syntax, context.IsGeneratedCode))
                action(context);
        };
    }

    public static Action<SyntaxNodeAnalysisContext> Confine(Action<SyntaxNodeAnalysisContext> action)
    {
        return context =>
        {
            if (Admits(context.Node, context.IsGeneratedCode))
                action(context);
        };
    }

    // Code outside generated files is checked as it always was. Generated code, and code behind #line
    // hidden that a build never shows anyone, is checked only where a Razor file's author wrote it.
    private static bool Admits(SyntaxNode node, bool generated)
    {
        SyntaxTree tree = node.SyntaxTree;
        bool generatedOrHidden = generated || (tree.HasHiddenRegions() && tree.GetLineVisibility(node.SpanStart) == LineVisibility.Hidden);

        return !generatedOrHidden || IsAuthored(node);
    }

    // A statement, member, or lambda is the smallest unit whose meaning its author decides. An expression
    // copied out of markup is an argument to a call Razor generates around it, so even a conversion a rule
    // objects to there is that call's doing, and only the code it sits in decides whether it counts.
    private static bool IsAuthored(SyntaxNode node)
    {
        SyntaxNode unit = node.FirstAncestorOrSelf<SyntaxNode>(static candidate => candidate is StatementSyntax or MemberDeclarationSyntax or AnonymousFunctionExpressionSyntax)
            ?? node;

        foreach (TextSpan span in AuthoredSpans.GetValue(node.SyntaxTree, static tree => new(Read(tree))).Value)
        {
            if (span.Contains(unit.Span))
                return true;
        }

        return false;
    }

    private static bool IsRazorFile(string path)
    {
        return path.EndsWith(value: ".razor", StringComparison.OrdinalIgnoreCase) || path.EndsWith(value: ".cshtml", StringComparison.OrdinalIgnoreCase);
    }

    private static ImmutableArray<TextSpan> Read(SyntaxTree tree)
    {
        TextLineCollection lines = tree.GetText().Lines;
        ImmutableArray<TextSpan>.Builder spans = ImmutableArray.CreateBuilder<TextSpan>();

        foreach (LineMapping mapping in tree.GetLineMappings())
        {
            bool copied = !mapping.IsHidden && mapping.MappedSpan.HasMappedPath && IsRazorFile(mapping.MappedSpan.Path)
                && mapping.Span.End.Line < lines.Count;

            if (!copied)
                continue;

            // An enhanced directive names the column its text starts at, because the generated call that
            // text is an argument to shares the first line, and Roslyn maps that call to the file as well.
            TextLine first = lines[mapping.Span.Start.Line];
            TextLine last = lines[mapping.Span.End.Line];
            int start = first.Start + Math.Min(mapping.CharacterOffset ?? 0, first.Span.Length);
            int end = Math.Min(last.Start + mapping.Span.End.Character, last.EndIncludingLineBreak);

            if (end > start)
                spans.Add(TextSpan.FromBounds(start, end));
        }

        return spans.ToImmutable();
    }
}
