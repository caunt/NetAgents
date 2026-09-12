using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Ordering;

namespace NetAgents.Analyzers.CodeFixes.Ordering;

/// <summary>
/// Sorts members while retaining attached trivia and initializer order.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MemberOrderingCodeFixProvider))]
[Shared]
public sealed class MemberOrderingCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [MemberOrdering.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return FixAllProvider.Create(static (context, document, diagnostics) => SortDocument(document, context.CancellationToken));
    }

    /// <inheritdoc />
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Sort type members",
                    createChangedDocument: cancellationToken => SortMembers(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                    equivalenceKey: nameof(MemberOrderingCodeFixProvider)
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    private static async Task<Document?> SortDocument(Document document, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root is null)
            return document;

        SyntaxNode updated = root.ReplaceNodes(
            root.DescendantNodes().OfType<TypeDeclarationSyntax>(),
            static (original, rewritten) => rewritten.WithMembers(MemberOrdering.Sort(rewritten))
        );

        return document.WithSyntaxRoot(updated);
    }

    private static async Task<Document> SortMembers(Document document, TextSpan span, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        TypeDeclarationSyntax? declaration = root?.FindToken(span.Start).Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();

        return root is null || declaration is null
            ? document
            : document.WithSyntaxRoot(root.ReplaceNode(declaration, declaration.WithMembers(MemberOrdering.Sort(declaration))));
    }
}
