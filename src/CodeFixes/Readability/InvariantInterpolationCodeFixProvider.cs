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
/// Writes an interpolated string alone where <c>string.Create(CultureInfo.InvariantCulture, …)</c> cannot change what
/// it prints.
/// </summary>
/// <remarks>
/// The string keeps every hole as written and takes the call's place, and is offered only while the analyzer's check
/// still holds against the current text.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InvariantInterpolationCodeFixProvider))]
[Shared]
public sealed class InvariantInterpolationCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [InvariantInterpolation.RuleIdentifier];

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
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not InvocationExpressionSyntax call)
                continue;

            InterpolatedStringExpressionSyntax? text = InvariantInterpolation.GetUnwrapped(call,
                model,
                context.CancellationToken);

            if (text is null)
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Write the string without string.Create",
                    cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(call, text))),
                    nameof(InvariantInterpolationCodeFixProvider)),
                diagnostic);
        }
    }
}
