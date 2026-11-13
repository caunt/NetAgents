using System.Collections.Immutable;
using System.Composition;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Documentation;

namespace NetAgents.Analyzers.CodeFixes.Documentation;

/// <summary>
/// Opens a constructor's summary with "Initializes a new instance of the" type it creates.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SummaryOpeningCodeFixProvider))]
[Shared]
public sealed class SummaryOpeningCodeFixProvider : CodeFixProvider
{
    // An opening already written the old way, with another article, a plain type name, or another kind
    // word, is replaced rather than kept beside the new one.
    private static readonly Regex WrittenOpening = new(pattern: @"^Initializes a new instance of (?:the|a|an) (?:<see\b[^>]*/>|\w+)(?: (?:class|struct|structure|record|type|object)(?![\w-]))?",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [DocumentationTags.SummaryOpeningRuleIdentifier];

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
            // A property's summary opens with a verb its author chooses the rest of the sentence for.
            if (!diagnostic.Properties.TryGetValue(DocumentationTags.CrefProperty, out string? cref) || cref is null)
                continue;

            if (!diagnostic.Properties.TryGetValue(DocumentationTags.KindProperty, out string? kind) || kind is null)
                continue;

            string opening = "Initializes a new instance of the <see cref=\"" + cref + "\"/> " + kind;

            context.RegisterCodeFix(CodeAction.Create(title: "Open the summary as the .NET documentation does",
                    cancellationToken => Open(context.Document, diagnostic.Location.SourceSpan, opening, cancellationToken),
                    nameof(SummaryOpeningCodeFixProvider)),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    // Where the summary's words start: past the line breaks and exterior markers of a wrapped comment.
    private static int? FindStart(XmlElementSyntax summary)
    {
        foreach (XmlNodeSyntax node in summary.Content)
        {
            if (node is not XmlTextSyntax text)
                return node.SpanStart;

            foreach (SyntaxToken token in text.TextTokens)
            {
                if (token.IsKind(SyntaxKind.XmlTextLiteralNewLineToken) || token.Text.Trim().Length == 0)
                    continue;

                return token.SpanStart + (token.Text.Length - token.Text.TrimStart().Length);
            }
        }

        return null;
    }

    private static async Task<Document> Open(Document document, TextSpan span, string opening, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document
            .GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        SourceText source = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root?.FindNode(span, findInsideTrivia: true, getInnermostNodeForTie: true) is not XmlElementSyntax summary)
            return document;

        int? start = FindStart(summary);

        if (start is not int position)
            return document.WithText(source.WithChanges(new TextChange(new TextSpan(summary.StartTag.Span.End, length: 0), opening + ".")));

        Match written = WrittenOpening.Match(source.ToString(TextSpan.FromBounds(position, summary.EndTag.SpanStart)));

        TextChange change = written.Success
            ? new TextChange(new TextSpan(position, written.Length), opening)
            : new TextChange(new TextSpan(position, length: 0), opening + ". ");

        return document.WithText(source.WithChanges(change));
    }
}
