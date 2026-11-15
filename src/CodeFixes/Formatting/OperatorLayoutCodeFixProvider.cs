using System.Collections.Generic;
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

using NetAgents.Analyzers.Formatting.Operators;

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Moves the line breaks of an <c>&amp;&amp;</c>, <c>||</c>, or <c>??</c> sequence or a <c>?:</c> expression to the
/// standard layout.
/// </summary>
/// <remarks>
/// An expression that fits loses every line break around its operators; one that must split gets a line break and one
/// level of indentation in front of each. The formatter then indents what an operand spreads over the lines below it.
/// The fix is offered only while the analyzer's check still holds against the current text.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OperatorLayoutCodeFixProvider))]
[Shared]
public sealed class OperatorLayoutCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [OperatorLayout.RuleIdentifier];

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
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not ExpressionSyntax node)
                continue;

            List<SyntaxToken>? operators = OperatorLayout.GetOperators(node);

            bool? split = operators is null
                ? null
                : OperatorLayout.GetRequiredSplit(node, operators, model, text, context.CancellationToken);

            if (operators is null || split is not bool required || OperatorLayout.IsSettled(node, operators, required, text))
                continue;

            SyntaxNode rewritten = OperatorLayout.Rewrite(root, node, operators, required, text);

            context.RegisterCodeFix(CodeAction.Create(title: "Lay out the operators",
                    cancellationToken => Format(context.Document, root, rewritten, node, cancellationToken),
                    nameof(OperatorLayoutCodeFixProvider)),
                diagnostic);
        }
    }

    private static async Task<Document> Format(Document document, SyntaxNode root, SyntaxNode rewritten, ExpressionSyntax node, CancellationToken cancellationToken)
    {
        SyntaxNode? statement = node.FirstAncestorOrSelf<StatementSyntax>();
        SyntaxNode scope = statement ?? node.FirstAncestorOrSelf<MemberDeclarationSyntax>() ?? (SyntaxNode)node;
        TextSpan span = TextSpan.FromBounds(scope.SpanStart, scope.Span.End + rewritten.FullSpan.Length - root.FullSpan.Length);

        return await Formatter.FormatAsync(document.WithSyntaxRoot(rewritten), span, cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
