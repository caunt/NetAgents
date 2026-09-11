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

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Collapses short argument and parameter lists and expands long lists without changing parameter values.
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
        return FixAllProvider.Create(
            static async (context, document, diagnostics) =>
            await FixArguments(document, requestedSpan: null, context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false)
        );
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root is null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            SyntaxNode list = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            if (!ArgumentLayout.IsList(list) || !ArgumentLayout.CanFix(list))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Fix argument and parameter layout",
                    createChangedDocument: cancellationToken => FixArguments(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                    equivalenceKey: nameof(ArgumentLayoutCodeFixProvider)
                ),
                diagnostic
            );
        }
    }

    private static async Task<Document> FixArguments(Document document, TextSpan? requestedSpan, CancellationToken cancellationToken)
    {
        bool hasChanges;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (root is null)
                return document;

            SourceText source = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            SyntaxNode? list = root.DescendantNodes().FirstOrDefault(
                node => ArgumentLayout.IsList(node)
                && !node.ContainsDiagnostics && ArgumentLayout.CanFix(node)
                && (requestedSpan is null || node.Span == requestedSpan)
                && !ArgumentLayout.HasExpectedLayout(node, source)
            );

            if (list is null)
                return document;

            SourceText changedSource = source.WithChanges(ArgumentLayout.GetChanges(list, source));
            TextSpan changedSpan = new(list.SpanStart, list.Span.Length + changedSource.Length - source.Length);
            document = await Formatter.FormatAsync(document.WithText(changedSource), changedSpan, cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);
            SourceText formattedSource = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            hasChanges = !source.ContentEquals(formattedSource);
        }
        while (requestedSpan is null && hasChanges);

        return document;
    }
}
