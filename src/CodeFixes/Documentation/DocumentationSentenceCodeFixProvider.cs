using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Documentation;

namespace NetAgents.Analyzers.CodeFixes.Documentation;

/// <summary>
/// Ends documentation text with the full stop it is missing.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DocumentationSentenceCodeFixProvider))]
[Shared]
public sealed class DocumentationSentenceCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [DocumentationTags.SentenceRuleIdentifier];

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
            context.RegisterCodeFix(CodeAction.Create(title: "Add a full stop",
                    cancellationToken => Punctuate(context.Document, diagnostic.Location.SourceSpan.Start, cancellationToken),
                    nameof(DocumentationSentenceCodeFixProvider)),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    // The report already sits where the sentence ends.
    private static async Task<Document> Punctuate(Document document, int position, CancellationToken cancellationToken)
    {
        SourceText source = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return document.WithText(source.WithChanges(new TextChange(new TextSpan(position, length: 0), newText: ".")));
    }
}
