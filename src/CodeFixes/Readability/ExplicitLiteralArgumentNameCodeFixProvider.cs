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

using NetAgents.Analyzers.Readability;

namespace NetAgents.Analyzers.CodeFixes.Readability;

/// <summary>
/// Names inline literal arguments and drops parameter names the rule does not require.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExplicitLiteralArgumentNameCodeFixProvider))]
[Shared]
public sealed class ExplicitLiteralArgumentNameCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [ExplicitLiteralArgumentName.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return FixAllProvider.Create(
            static async (context, document, diagnostics) =>
            await FixNames(document, requestedSpans: null, fixAll: true, context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false)
        );
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        SemanticModel? model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root is null || model is null)
            return;

        ImmutableDictionary<TextSpan, ExplicitLiteralArgumentName.ArgumentNaming> namings =
            ExplicitLiteralArgumentName.GetChanges(root, model, context.CancellationToken);

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            // An action that changes nothing stalls the build formatter, so unfixable shapes stay unregistered.
            // An action that changes nothing stalls the build formatter, so unfixable shapes stay unregistered.
            // An action that changes nothing stalls the build formatter, so unfixable shapes stay unregistered.
            // An action that changes nothing stalls the build formatter, so unfixable shapes stay unregistered.
            if (!namings.ContainsKey(diagnostic.Location.SourceSpan))
                continue;

            TextSpan span = diagnostic.Location.SourceSpan;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Fix argument parameter names",
                    cancellationToken => FixNames(context.Document, [span], fixAll: false, cancellationToken),
                    nameof(ExplicitLiteralArgumentNameCodeFixProvider)
                ),
                diagnostic
            );
        }
    }

    private static async Task<Document> FixNames(Document document, ImmutableHashSet<TextSpan>? requestedSpans, bool fixAll, CancellationToken cancellationToken)
    {
        bool hasChanges = true;

        while (hasChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (root is null || model is null)
                return document;

            ExplicitLiteralArgumentName.ArgumentNaming[] selected = SelectOutermost(ExplicitLiteralArgumentName.GetChanges(root, model, cancellationToken), requestedSpans);

            if (selected.Length == 0)
                return document;

            SourceText source = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            document = document.WithSyntaxRoot(ReplaceLists(root, selected));
            SourceText changed = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            hasChanges = fixAll && !source.ContentEquals(changed);
            requestedSpans = null;
        }

        return document;
    }

    private static SyntaxNode ReplaceLists(SyntaxNode root, ExplicitLiteralArgumentName.ArgumentNaming[] namings)
    {
        Dictionary<SyntaxNode, BaseArgumentListSyntax> replacements = [];

        foreach (ExplicitLiteralArgumentName.ArgumentNaming naming in namings)
            replacements[naming.List] = naming.Replacement;

        return root.ReplaceNodes(replacements.Keys, (original, rewritten) => replacements[original]);
    }

    private static ExplicitLiteralArgumentName.ArgumentNaming[] SelectOutermost(ImmutableDictionary<TextSpan, ExplicitLiteralArgumentName.ArgumentNaming> namings, ImmutableHashSet<TextSpan>? requestedSpans)
    {
        ExplicitLiteralArgumentName.ArgumentNaming[] candidates = [.. namings
            .Where(naming => requestedSpans is null || requestedSpans.Contains(naming.Key))
            .Select(static naming => naming.Value)
            .Distinct()];

        // Replacements are built from the original lists, so a nested list waits for the next pass.
        return [.. candidates.Where(naming => !candidates.Any(other => other.List.Span != naming.List.Span && other.List.Span.Contains(naming.List.Span)))];
    }
}
