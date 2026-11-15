using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting.Clauses;

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Joins an assigned value onto the line of its <c>=</c>.
/// </summary>
/// <remarks>
/// The value takes one space after the <c>=</c>, and the formatter then indents what the value spreads over the lines
/// below it under the line it is now on. The fix is offered only while the analyzer's check still holds against the
/// current text.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ValuePlacementCodeFixProvider))]
[Shared]
public sealed class ValuePlacementCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [ValuePlacement.RuleIdentifier];

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

        SourceText text = await context.Document
            .GetTextAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (root is null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            SyntaxNode? reported = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;

            if (reported is null || !ValuePlacement.IsDetached(reported, text))
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Start the value on the line of its =",
                    cancellationToken => Join(context.Document, root, reported, cancellationToken),
                    nameof(ValuePlacementCodeFixProvider)), diagnostic);
        }
    }

    private static async Task<Document> Join(Document document, SyntaxNode root, SyntaxNode node, CancellationToken cancellationToken)
    {
        SyntaxNode joined = ValuePlacement.Join(root, node);
        SyntaxNode? statement = node.FirstAncestorOrSelf<StatementSyntax>();
        SyntaxNode scope = statement ?? node.FirstAncestorOrSelf<MemberDeclarationSyntax>() ?? node;
        TextSpan span = TextSpan.FromBounds(scope.SpanStart, scope.Span.End + joined.FullSpan.Length - root.FullSpan.Length);

        return await Formatter.FormatAsync(document.WithSyntaxRoot(joined), span, cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
