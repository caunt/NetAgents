using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
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
/// Removes documentation tags that do not fit their member and puts parameter tags in declaration order.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DocumentationTagFitCodeFixProvider))]
[Shared]
public sealed class DocumentationTagFitCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [DocumentationTags.TagFitRuleIdentifier];

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
            // An inheritdoc with nothing to inherit needs text only its author can write.
            if (!diagnostic.Properties.TryGetValue(DocumentationTags.FixProperty, out string? fix) || fix is null)
                continue;

            string title = fix == DocumentationTags.Order ? "Order the tags as the declaration does" : "Remove the tag";

            context.RegisterCodeFix(CodeAction.Create(title,
                    cancellationToken => Repair(context.Document, diagnostic.Location.SourceSpan, fix, cancellationToken),
                    nameof(DocumentationTagFitCodeFixProvider) + fix),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    // An element alone on its lines takes those lines with it, so no empty "///" line is left behind;
    // one sharing a line with other text leaves the rest of that line as written.
    private static TextSpan GetRemovalSpan(SourceText source, TextSpan element)
    {
        TextLine first = source.Lines.GetLineFromPosition(element.Start);
        TextLine last = source.Lines.GetLineFromPosition(element.End);
        string before = source.ToString(TextSpan.FromBounds(first.Start, element.Start)).Trim();
        string after = source.ToString(TextSpan.FromBounds(element.End, last.End)).Trim();

        return before == "///" && after.Length == 0 ? TextSpan.FromBounds(first.Start, last.EndIncludingLineBreak) : element;
    }

    // The elements keep the places they occupy and only trade text, so the comment's layout is kept.
    private static IEnumerable<TextChange> Order(SourceText source, XmlNodeSyntax reported)
    {
        DocumentationCommentTriviaSyntax? comment = reported.FirstAncestorOrSelf<DocumentationCommentTriviaSyntax>();
        SyntaxNode? declaration = comment is null ? null : DocumentationTags.FindDeclaration(comment);
        string element = DocumentationTags.GetName(reported) ?? string.Empty;

        if (declaration is null)
            return [];

        ImmutableArray<string> declared = element == "typeparam"
            ? DocumentationTags.GetTypeParameterNames(declaration)
            : DocumentationTags.GetParameterNames(declaration);

        List<XmlNodeSyntax> written = DocumentationTags.GetNamed(DocumentationTags.GetElements(declaration), element);

        if (DocumentationTags.FindMisordered(DocumentationTags.GetElements(declaration), element, declared) is null)
            return [];

        List<XmlNodeSyntax> ordered = [.. written.OrderBy(node => declared.IndexOf(DocumentationTags.GetAttribute(node, attribute: "name") ?? string.Empty))];

        return written.Select((node, index) => new TextChange(node.Span, source.ToString(ordered[index].Span)));
    }

    private static async Task<Document> Repair(Document document, TextSpan span, string fix, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        SourceText source = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root?.FindNode(span, findInsideTrivia: true, getInnermostNodeForTie: true) is not XmlNodeSyntax reported)
            return document;

        IEnumerable<TextChange> changes = string.Equals(fix, DocumentationTags.Order, StringComparison.Ordinal)
            ? Order(source, reported)
            : [new TextChange(GetRemovalSpan(source, reported.Span), string.Empty)];

        return document.WithText(source.WithChanges(changes));
    }
}
