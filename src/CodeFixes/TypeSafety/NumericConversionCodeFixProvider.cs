using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.TypeSafety;

namespace NetAgents.Analyzers.CodeFixes.TypeSafety;

/// <summary>
/// Rewrites numeric casts into the generic math factory that keeps their behavior: <c>(int)value</c>
/// becomes <c>int.CreateTruncating(value)</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NumericConversionCodeFixProvider))]
[Shared]
public sealed class NumericConversionCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [NumericConversion.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return FixAllProvider.Create(static async (context, document, diagnostics) =>
            await Convert(document, diagnostics, context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false));
    }

    /// <inheritdoc />
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(CodeAction.Create(title: "Convert with a numeric factory method",
                    cancellationToken => Convert(context.Document, diagnostics: [diagnostic], cancellationToken),
                    nameof(NumericConversionCodeFixProvider)),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> Convert(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root is null || model is null)
            return document;

        Dictionary<SyntaxNode, List<Replacement>> replacements = [];

        foreach (Diagnostic diagnostic in diagnostics)
        {
            SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            // The call the fix writes needs no parentheses, so it replaces any that enclose the conversion.
            SyntaxNode target = node;

            while (target.Parent is ParenthesizedExpressionSyntax parenthesized)
                target = parenthesized;

            if (!replacements.ContainsKey(target) && Plan(node, model, cancellationToken) is { Count: > 0 } steps)
                replacements.Add(target, steps);
        }

        if (replacements.Count == 0)
            return document;

        // Nested casts are rewritten inside out, so an outer one wraps the call its operand became.
        SyntaxNode rewritten = root.ReplaceNodes(replacements.Keys, (original, current) => Rewrite(current, replacements[original]));

        return document.WithSyntaxRoot(rewritten);
    }

    // The cast the diagnostic names. An implicit conversion of the cast's result shares its syntax and is
    // passed over, since the rule leaves implicit conversions alone and the call returns the cast's type.
    private static List<Replacement> Plan(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        List<Replacement> steps = [];

        for (IOperation? current = model.GetOperation(node, cancellationToken); current is not null && current.Syntax == node; current = current.Parent)
        {
            if (current is not IConversionOperation conversion)
                continue;

            if (NumericConversion.GetFactory(conversion, model, cancellationToken) is not string factory)
                continue;

            string receiver = !conversion.IsImplicit && node is CastExpressionSyntax cast
                ? cast.Type.ToString()
                : conversion.Type?.ToMinimalDisplayString(model, node.SpanStart) ?? string.Empty;

            if (receiver.Length == 0)
                return [];

            steps.Add(new Replacement(factory, receiver, !conversion.IsImplicit));
        }

        return steps;
    }

    private static ExpressionSyntax Rewrite(SyntaxNode current, List<Replacement> steps)
    {
        ExpressionSyntax expression = (ExpressionSyntax)current;

        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        foreach (Replacement step in steps)
        {
            ExpressionSyntax value = step.Written && expression is CastExpressionSyntax cast ? cast.Expression : expression;

            while (value is ParenthesizedExpressionSyntax parenthesized)
                value = parenthesized.Expression;

            expression = SyntaxFactory.ParseExpression(step.Receiver + "." + step.Factory + "(" + value.WithoutTrivia().ToFullString() + ")");
        }

        return expression.WithTriviaFrom(current);
    }

    private sealed class Replacement(string factory, string receiver, bool written)
    {
        public string Factory { get; } = factory;
        public string Receiver { get; } = receiver;
        public bool Written { get; } = written;
    }
}
