using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting.Statements;

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Joins a statement that fits within 128 characters onto one line.
/// </summary>
/// <remarks>
/// Every line break inside the statement becomes a space, and the formatter then settles the spacing as it would on
/// any line, so <c>( await task )</c> reads <c>(await task)</c> and a chain broken before each dot reads as one
/// expression. The fix is offered only while the analyzer's check still holds against the current text.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OneLineStatementCodeFixProvider))]
[Shared]
public sealed class OneLineStatementCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [OneLineStatement.RuleIdentifier];

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

        SourceText text = await context.Document
            .GetTextAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        SemanticModel? model = await context.Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (root is null || model is null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            StatementSyntax? statement = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.FirstAncestorOrSelf<StatementSyntax>();

            if (statement is null || !OneLineStatement.IsJoinable(statement, model, text, context.CancellationToken))
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Write the statement on one line",
                    cancellationToken => Join(context.Document, root, statement, cancellationToken),
                    nameof(OneLineStatementCodeFixProvider)),
                diagnostic);
        }
    }

    private static async Task<Document> Join(Document document, SyntaxNode root, StatementSyntax statement, CancellationToken cancellationToken)
    {
        StatementSyntax joined = OneLineStatement.Join(statement).WithAdditionalAnnotations(Formatter.Annotation);
        Document updated = document.WithSyntaxRoot(root.ReplaceNode(statement, joined));

        return await Formatter
            .FormatAsync(updated, Formatter.Annotation, cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
