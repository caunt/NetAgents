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
/// Replaces a delegate constructor with the lambda, anonymous method, or method group it wraps.
/// </summary>
/// <remarks>
/// The wrapped expression takes the constructor's place and trivia. The fix is offered only while the analyzer's check
/// still holds against the current text.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DelegateCreationCodeFixProvider))]
[Shared]
public sealed class DelegateCreationCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [DelegateCreation.RuleIdentifier];

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
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not BaseObjectCreationExpressionSyntax creation)
                continue;

            ExpressionSyntax? wrapped = DelegateCreation.GetWrapped(creation, model, context.CancellationToken);

            if (wrapped is null)
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Assign the lambda or method group directly",
                    cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(creation, wrapped.WithTriviaFrom(creation)))),
                    nameof(DelegateCreationCodeFixProvider)),
                diagnostic);
        }
    }
}
