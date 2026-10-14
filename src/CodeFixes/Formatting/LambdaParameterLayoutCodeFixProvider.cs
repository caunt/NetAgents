using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting;

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Removes the optional parentheses around a lambda's single inferred parameter.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LambdaParameterLayoutCodeFixProvider))]
[Shared]
public sealed class LambdaParameterLayoutCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [LambdaParameterLayout.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc />
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Drop the lambda parameter parentheses",
                    cancellationToken => DropParentheses(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                    nameof(LambdaParameterLayoutCodeFixProvider)
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> DropParentheses(Document document, TextSpan diagnosticSpan, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root is null)
            return document;

        // The diagnostic sits on the parameter list, so the lambda it belongs to is its parent.
        ParameterListSyntax? parameters = root.FindNode(diagnosticSpan) as ParameterListSyntax;
        ParenthesizedLambdaExpressionSyntax? lambda = parameters?.Parent as ParenthesizedLambdaExpressionSyntax;

        return lambda is not null && LambdaParameterLayout.IsViolation(lambda)
            ? document.WithSyntaxRoot(root.ReplaceNode(lambda, LambdaParameterLayout.Simplify(lambda)))
            : document;
    }
}
