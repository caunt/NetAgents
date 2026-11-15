using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Readability;

namespace NetAgents.Analyzers.CodeFixes.Readability;

/// <summary>
/// Writes string literals and interpolated strings joined with <c>+</c> on one line as one string.
/// </summary>
/// <remarks>
/// The merged string keeps every escape and hole as written, and is offered only while the analyzer's check still
/// holds against the current text.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LiteralConcatenationCodeFixProvider))]
[Shared]
public sealed class LiteralConcatenationCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [LiteralConcatenation.RuleIdentifier];

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

        SemanticModel? model = await context.Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        SourceText text = await context.Document
            .GetTextAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (root is null || model is null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not BinaryExpressionSyntax addition)
                continue;

            ExpressionSyntax? merged = LiteralConcatenation.GetMerged(addition, model, text, context.CancellationToken);

            if (merged is null)
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Write the strings as one",
                    cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(addition, merged))),
                    nameof(LiteralConcatenationCodeFixProvider)),
                diagnostic);
        }
    }
}
