using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.StatementGroups;

namespace NetAgents.Analyzers.CodeFixes.StatementGroups;

/// <summary>
/// Moves blank lines so statements of one kind sit together and one blank line separates each group.
/// </summary>
/// <remarks>
/// Only blank lines change: statements and comments keep their order and text, a blank line goes directly above
/// the first comment line or statement of each group, and the rest are removed.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StatementGroupingCodeFixProvider))]
[Shared]
public sealed class StatementGroupingCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [StatementGrouping.RuleIdentifier];

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
            context.RegisterCodeFix(CodeAction.Create(title: "Group statements with blank lines",
                    cancellationToken => Regroup(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                    nameof(StatementGroupingCodeFixProvider)),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> Regroup(Document document, TextSpan diagnosticSpan, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        SemanticModel? model = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (root is null || model is null)
            return document;

        SourceText source = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return StatementGrouping.GetChanges(root, model, source, cancellationToken).TryGetValue(diagnosticSpan, out TextChange change)
            ? document.WithText(source.WithChanges(change))
            : document;
    }
}
