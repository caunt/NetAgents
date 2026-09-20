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
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting;

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Adds or removes body braces while preserving comments, line endings, and conditional binding.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ControlFlowBracesCodeFixProvider))]
[Shared]
public sealed class ControlFlowBracesCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [ControlFlowBraces.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return FixAllProvider.Create(
            static async (context, document, diagnostics) =>
            await FixBraces(document, diagnostics, fixAll: true, context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false)
        );
    }

    /// <inheritdoc />
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Fix body braces",
                    cancellationToken => FixBraces(context.Document, [diagnostic], fixAll: false, cancellationToken),
                    nameof(ControlFlowBracesCodeFixProvider)
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> ApplyBraceChanges(Document document, SourceText source, SyntaxNode[] bodies, CancellationToken cancellationToken)
    {
        List<TextChange> changes = [];
        List<TextSpan> affectedSpans = [];
        TextLine firstLine = source.Lines[index: 0];

        string lineEnding = firstLine.EndIncludingLineBreak > firstLine.End
            ? source.ToString(TextSpan.FromBounds(firstLine.End, firstLine.EndIncludingLineBreak))
            : "\n";

        foreach (SyntaxNode body in bodies)
        {
            if (body is BlockSyntax block)
            {
                changes.Add(RemoveBrace(block.OpenBraceToken, source));
                changes.Add(RemoveBrace(block.CloseBraceToken, source));
            }
            else
            {
                TextSpan span = body is SwitchSectionSyntax section
                    ? TextSpan.FromBounds(section.Statements[index: 0].FullSpan.Start, section.Statements.Last().FullSpan.End)
                    : body.FullSpan;

                changes.Add(InsertBrace(span.Start, brace: "{", source, lineEnding));
                changes.Add(InsertBrace(span.End, brace: "}", source, lineEnding));
            }

            affectedSpans.Add((body is SwitchSectionSyntax ? body : body.Parent ?? body).FullSpan);
        }

        TextChange[] orderedChanges = [.. changes.OrderBy(static change => change.Span.Start).ThenBy(static change => change.Span.Length)];
        Document changedDocument = document.WithText(source.WithChanges(orderedChanges));

        IEnumerable<TextSpan> formattingSpans = affectedSpans.Select(
            span => TextSpan.FromBounds(
                TranslatePosition(span.Start, orderedChanges, includeInsertions: false),
                TranslatePosition(span.End, orderedChanges, includeInsertions: true)
            )
        );

        OptionSet options = await document.GetOptionsAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        options = options.WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, lineEnding);

        return await Formatter.FormatAsync(changedDocument, formattingSpans, options, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    private static async Task<Document> FixBraces(Document document, ImmutableArray<Diagnostic> diagnostics, bool fixAll, CancellationToken cancellationToken)
    {
        ImmutableHashSet<TextSpan>? requestedSpans = [.. diagnostics.Select(static diagnostic => diagnostic.Location.SourceSpan)];
        bool hasChanges = true;

        while (hasChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (root is null)
                return document;

            SourceText source = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            SyntaxNode[] bodies = [.. ControlFlowBraces.GetChanges(root, source)
                .Where(change => requestedSpans is null || requestedSpans.Contains(change.Key))
                .Select(static change => change.Value)];

            if (bodies.Length == 0)
                return document;

            document = await ApplyBraceChanges(document, source, bodies, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            SourceText changedSource = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            hasChanges = fixAll && !source.ContentEquals(changedSource);
            requestedSpans = null;
        }

        return document;
    }

    private static TextChange InsertBrace(int position, string brace, SourceText source, string lineEnding)
    {
        string prefix = position > 0 && source[position - 1] is not ('\r' or '\n') ? lineEnding : string.Empty;

        return new TextChange(new TextSpan(position, length: 0), prefix + brace + lineEnding);
    }

    private static TextChange RemoveBrace(SyntaxToken brace, SourceText source)
    {
        TextLine line = source.Lines.GetLineFromPosition(brace.SpanStart);

        bool ownsLine = source.ToString(TextSpan.FromBounds(line.Start, brace.SpanStart)).All(char.IsWhiteSpace)
            && source.ToString(TextSpan.FromBounds(brace.Span.End, line.End)).All(char.IsWhiteSpace);

        return new TextChange(ownsLine ? line.SpanIncludingLineBreak : brace.Span, string.Empty);
    }

    private static int TranslatePosition(int position, TextChange[] changes, bool includeInsertions)
    {
        int translated = position;

        foreach (TextChange change in changes)
        {
            if (change.Span.Start > position || (change.Span.Start == position && !includeInsertions))
                break;

            translated += (change.NewText?.Length ?? 0) - (System.Math.Min(position, change.Span.End) - change.Span.Start);
        }

        return translated;
    }
}
