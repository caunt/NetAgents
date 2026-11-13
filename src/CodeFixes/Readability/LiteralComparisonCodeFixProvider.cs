using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using NetAgents.Analyzers.Readability;

namespace NetAgents.Analyzers.CodeFixes.Readability;

/// <summary>
/// Replaces a <c>string.Equals</c> call against a string literal with a constant pattern.
/// </summary>
/// <remarks>
/// A <c>!</c> in front of the call becomes <c>is not</c>. The fix is offered only while the analyzer's check still
/// holds against the current text.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LiteralComparisonCodeFixProvider))]
[Shared]
public sealed class LiteralComparisonCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [LiteralComparison.RuleIdentifier];

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

        if (root is null || model is null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not InvocationExpressionSyntax invocation)
                continue;

            ExpressionSyntax? pattern = LiteralComparison.GetPattern(invocation, model, context.CancellationToken);

            if (pattern is null)
                continue;

            ExpressionSyntax replaced = LiteralComparison.GetReplaced(invocation);

            context.RegisterCodeFix(CodeAction.Create(title: "Compare through a pattern",
                    cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(replaced, pattern))),
                    nameof(LiteralComparisonCodeFixProvider)),
                diagnostic);
        }
    }
}
