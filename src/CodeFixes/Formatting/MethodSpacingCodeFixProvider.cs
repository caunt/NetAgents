using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting;

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Inserts missing blank lines between method declarations.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MethodSpacingCodeFixProvider))]
[Shared]
public sealed class MethodSpacingCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [MethodSpacing.RuleIdentifier];

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
                    title: "Insert blank line between method declarations",
                    cancellationToken => InsertBlankLine(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                    nameof(MethodSpacingCodeFixProvider)
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> InsertBlankLine(Document document, TextSpan diagnosticSpan, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root is null)
            return document;

        SourceText source = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return MethodSpacing.GetChanges(root, source).TryGetValue(diagnosticSpan, out TextChange change)
            ? document.WithText(source.WithChanges(change))
            : document;
    }
}
