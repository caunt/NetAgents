using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Formatting.Collections;

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Collapses short argument and parameter lists, collection expressions, and initializers, and expands long ones without
/// changing what they hold.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ArgumentLayoutCodeFixProvider))]
[Shared]
public sealed class ArgumentLayoutCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [ArgumentLayout.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return FixAllProvider.Create(static async (context, document, diagnostics) => await FixArguments(document,
            requestedSpan: null,
            context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false));
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (root is null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            SyntaxNode list = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            bool fixable = ArgumentLayout.IsList(list) ? ArgumentLayout.CanFix(list) : CollectionLayout.CanLayOut(list);

            if (!fixable)
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Fix argument and parameter layout",
                    cancellationToken => FixArguments(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                    nameof(ArgumentLayoutCodeFixProvider)), diagnostic);
        }
    }

    private static async Task<Document> FixArguments(Document document, TextSpan? requestedSpan, CancellationToken cancellationToken)
    {
        bool hasChanges;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            if (root is null)
                return document;

            SourceText source = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            if (model is null)
                return document;

            SyntaxNode? list = root.DescendantNodes()
                .FirstOrDefault(node => NeedsLayout(node, requestedSpan, model, source, cancellationToken));

            if (list is null)
                return document;

            TextChange[] changes = ArgumentLayout.IsList(list)
                ? ArgumentLayout.GetChanges(list, model, source, cancellationToken)
                : CollectionLayout.GetChanges(list, source);

            SourceText changedSource = source.WithChanges(changes);
            int start = Math.Min(list.SpanStart, changes[0].Span.Start);
            TextSpan changedSpan = TextSpan.FromBounds(start, list.Span.End + changedSource.Length - source.Length);

            document = await Formatter.FormatAsync(document.WithText(changedSource), changedSpan, cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            SourceText formattedSource = await document.GetTextAsync(cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            hasChanges = !source.ContentEquals(formattedSource);
        }
        while (requestedSpan is null && hasChanges);

        return document;
    }

    private static bool NeedsLayout(SyntaxNode node, TextSpan? requestedSpan, SemanticModel model, SourceText source, CancellationToken cancellationToken)
    {
        if (requestedSpan is TextSpan span && node.Span != span)
            return false;

        if (!ArgumentLayout.IsList(node))
            return CollectionLayout.CanLayOut(node) && !CollectionLayout.HasExpectedLayout(node, source);

        bool fixable = !node.ContainsDiagnostics && ArgumentLayout.CanFix(node);

        return fixable && !ArgumentLayout.HasExpectedLayout(node, model, source, cancellationToken);
    }
}
