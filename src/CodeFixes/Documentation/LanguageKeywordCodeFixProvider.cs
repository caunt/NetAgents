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
/// Writes <see langword="null"/>, <see langword="true"/>, and <see langword="false"/> in documentation text as <c>langword</c> references.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LanguageKeywordCodeFixProvider))]
[Shared]
public sealed class LanguageKeywordCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [DocumentationTags.KeywordRuleIdentifier];

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
            // A Boolean description worded another way needs its author; only a keyword is rewritten.
            if (!diagnostic.Properties.TryGetValue(DocumentationTags.KeywordProperty, out string? keyword) || keyword is null)
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Write the keyword as a langword reference",
                    cancellationToken => Replace(context.Document, diagnostic.Location.SourceSpan, keyword, cancellationToken),
                    nameof(LanguageKeywordCodeFixProvider)),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> Replace(Document document, TextSpan span, string keyword, CancellationToken cancellationToken)
    {
        SourceText source = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return document.WithText(source.WithChanges(new TextChange(span, "<see langword=\"" + keyword + "\"/>")));
    }
}
