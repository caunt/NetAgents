using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

using NetAgents.Analyzers.Formatting.Clauses;

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Moves a <c>when</c> keyword up behind the clause it guards, one space after it.
/// </summary>
/// <remarks>
/// <c>catch (IOException exception)</c> followed by <c>when (…)</c> on the next line becomes
/// <c>catch (IOException exception) when (…)</c>. The fix is offered only when nothing but whitespace stands
/// between the two; a comment there stays where its author put it, and the diagnostic stays until they move it.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(WhenClausePlacementCodeFixProvider))]
[Shared]
public sealed class WhenClausePlacementCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [WhenClausePlacement.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root is null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            SyntaxToken keyword = root.FindToken(diagnostic.Location.SourceSpan.Start);

            if (!WhenClausePlacement.CanMove(keyword))
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Move 'when' up behind its clause",
                    cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(WhenClausePlacement.MoveUp(root, keyword))),
                    nameof(WhenClausePlacementCodeFixProvider)),
                diagnostic);
        }
    }
}
