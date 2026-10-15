using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Documentation;

namespace NetAgents.Analyzers.CodeFixes.Documentation;

/// <summary>
/// Wraps a documentation comment's loose prose in a summary element.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DocumentationProseCodeFixProvider))]
[Shared]
public sealed class DocumentationProseCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [DocumentationProse.RuleIdentifier];

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
                    title: "Wrap the description in a summary element",
                    cancellationToken => Wrap(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                    nameof(DocumentationProseCodeFixProvider)
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    // The comment is rewritten through its text rather than its XML nodes. Documentation trivia
    // carries its exterior "///" markers inside the tokens, so building a summary element out of
    // the existing nodes reflows every marker; replacing the prose lines leaves the rest untouched.
    private static async Task<Document> Wrap(Document document, TextSpan diagnosticSpan, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        SourceText? source = root is null ? null : await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root is null || source is null)
            return document;

        SyntaxNode? located = root.FindNode(diagnosticSpan, findInsideTrivia: true);
        DocumentationCommentTriviaSyntax? documentation = located?.FirstAncestorOrSelf<DocumentationCommentTriviaSyntax>();

        if (documentation is null)
            return document;

        XmlTextSyntax? prose = DocumentationProse.FindLooseProse(documentation);

        if (prose is null)
            return document;

        TextSpan span = DocumentationProse.GetProseSpan(prose);
        string raw = source.ToString(span);
        int leading = raw.Length - raw.TrimStart().Length;
        int trailing = raw.Length - raw.TrimEnd().Length;

        if (leading + trailing >= raw.Length)
            return document;

        // Only the written words are enclosed, so the indentation and the exterior markers framing
        // them keep the layout the author chose around the element being introduced.
        TextSpan written = TextSpan.FromBounds(span.Start + leading, span.End - trailing);
        string text = source.ToString(written);

        return document.WithText(source.WithChanges(new TextChange(written, "<summary>" + text + "</summary>")));
    }
}
