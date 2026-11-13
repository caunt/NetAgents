using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting.Chains;

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Moves the line breaks of a member chain to the standard layout.
/// </summary>
/// <remarks>
/// A chain that fits loses every line break before a dot; one that must split gets a line break before every link,
/// and none inside a namespace or type qualifier of its receiver. Nothing else in the chain changes. The fix is offered
/// only while the analyzer's check still holds against the current text.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ChainLayoutCodeFixProvider))]
[Shared]
public sealed class ChainLayoutCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [ChainLayout.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context
            .Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        SemanticModel? model = await context
            .Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        SourceText text = await context
            .Document
            .GetTextAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (root is null || model is null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not ExpressionSyntax top)
                continue;

            List<ChainLink>? links = ChainLayout.GetLinks(top, model, text, context.CancellationToken);
            bool? split = links is null ? null : ChainLayout.GetRequiredSplit(top, links, text);

            if (links is null || split is not bool required || ChainLayout.IsSettled(links, required, text))
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Lay out the chain",
                    cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(ChainLayout.Rewrite(root, links, required, text))),
                    nameof(ChainLayoutCodeFixProvider)),
                diagnostic);
        }
    }
}
