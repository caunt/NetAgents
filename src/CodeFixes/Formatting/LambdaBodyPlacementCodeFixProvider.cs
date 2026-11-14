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
/// Joins the body of an expression-bodied lambda onto the line of its <c>=&gt;</c>.
/// </summary>
/// <remarks>
/// The body takes one space after the arrow, and the formatter then indents what the body spreads over the lines below
/// it, such as an initializer's braces, under the line it is now on. The fix is offered only while the analyzer's
/// check still holds against the current text.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LambdaBodyPlacementCodeFixProvider))]
[Shared]
public sealed class LambdaBodyPlacementCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [LambdaBodyPlacement.RuleIdentifier];

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

            if (reported is not LambdaExpressionSyntax lambda || !LambdaBodyPlacement.IsDetached(lambda, text))
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Start the body on the arrow's line",
                    cancellationToken => Join(context.Document, root, lambda, cancellationToken),
                    nameof(LambdaBodyPlacementCodeFixProvider)),
                diagnostic);
        }
    }

    private static async Task<Document> Join(Document document, SyntaxNode root, LambdaExpressionSyntax lambda, CancellationToken cancellationToken)
    {
        SyntaxNode joined = LambdaBodyPlacement.Join(root, lambda);
        SyntaxNode? statement = lambda.FirstAncestorOrSelf<StatementSyntax>();
        SyntaxNode scope = statement ?? lambda.FirstAncestorOrSelf<MemberDeclarationSyntax>() ?? (SyntaxNode)lambda;
        TextSpan span = TextSpan.FromBounds(scope.SpanStart, scope.Span.End + joined.FullSpan.Length - root.FullSpan.Length);

        return await Formatter.FormatAsync(document.WithSyntaxRoot(joined), span, cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
