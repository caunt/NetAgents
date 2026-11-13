using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

using NetAgents.Analyzers.Readability;

namespace NetAgents.Analyzers.CodeFixes.Readability;

/// <summary>
/// Merges a <c>try</c> that only adds a <c>finally</c> with the <c>try</c> it wraps.
/// </summary>
/// <remarks>
/// The merged statement keeps the outer statement's position and <c>finally</c> and takes the inner block and handlers,
/// which the formatter then indents one level shallower.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NestedTryCodeFixProvider))]
[Shared]
public sealed class NestedTryCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [NestedTry.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
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
            SyntaxNode? reported = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;

            if (reported is not TryStatementSyntax outer || NestedTry.GetMergeable(outer) is null)
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Merge the try statements",
                    cancellationToken => Merge(context.Document, root, outer, cancellationToken),
                    nameof(NestedTryCodeFixProvider)),
                diagnostic);
        }
    }

    private static async Task<Document> Merge(Document document, SyntaxNode root, TryStatementSyntax outer, CancellationToken cancellationToken)
    {
        TryStatementSyntax? inner = NestedTry.GetMergeable(outer);

        if (inner is null)
            return document;

        TryStatementSyntax merged = NestedTry.Merge(outer, inner).WithAdditionalAnnotations(Formatter.Annotation);
        Document changed = document.WithSyntaxRoot(root.ReplaceNode(outer, merged));

        return await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
