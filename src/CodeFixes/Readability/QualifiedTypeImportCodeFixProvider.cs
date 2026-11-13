using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Readability;

namespace NetAgents.Analyzers.CodeFixes.Readability;

/// <summary>
/// Imports a qualified type's namespace and shortens the reference to the type name.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(QualifiedTypeImportCodeFixProvider))]
[Shared]
public sealed class QualifiedTypeImportCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [QualifiedTypeImport.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        // Each reference in a document rewrites the same import list, so a batch of separate edits
        // conflicts and only part of it survives a pass. Rewriting the document once per pass lets
        // the whole file settle immediately instead of costing a re-analysis per surviving edit.
        return FixAllProvider.Create(static async (context, document, diagnostics) =>
            await ImportAll(document, diagnostics, context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false));
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        SemanticModel? model = await context.Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (root is null || model is null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            // A reference whose name is already taken needs an alias the author chooses, so no action
            // is offered for it; registering one that cannot apply would stall the build formatter.
            if (!IsFixable(root, model, diagnostic.Location.SourceSpan, context.CancellationToken))
                continue;

            TextSpan span = diagnostic.Location.SourceSpan;

            context.RegisterCodeFix(CodeAction.Create(title: "Import the type's namespace",
                    cancellationToken => Import(context.Document, span, cancellationToken),
                    nameof(QualifiedTypeImportCodeFixProvider)),
                diagnostic);
        }
    }

    private static CompilationUnitSyntax AddImport(CompilationUnitSyntax unit, string namespaceName, SyntaxTrivia lineEnding)
    {
        SyntaxList<UsingDirectiveSyntax> usings = unit.Usings;

        foreach (UsingDirectiveSyntax existing in usings)
        {
            if (existing.Alias is null && string.Equals(existing.Name?.ToString(), namespaceName, StringComparison.Ordinal))
                return unit;
        }

        UsingDirectiveSyntax directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(lineEnding);

        int index = FindIndex(usings, namespaceName);

        // The first directive carries anything above it, such as a file header, so that stays on top.
        if (index == 0 && usings.Count > 0)
        {
            UsingDirectiveSyntax first = usings[index: 0];

            directive = directive.WithLeadingTrivia(first.GetLeadingTrivia());
            usings = usings.Replace(first, first.WithoutLeadingTrivia());
        }

        return unit.WithUsings(usings.Insert(index, directive));
    }

    // The reported reference: a qualified type name, or the member access that reaches a type through
    // its namespace.
    private static ExpressionSyntax? Find(SyntaxNode root, TextSpan diagnosticSpan)
    {
        return root.FindNode(diagnosticSpan, getInnermostNodeForTie: true) switch
        {
            QualifiedNameSyntax name => name,
            MemberAccessExpressionSyntax access => access,
            _ => null,
        };
    }

    // System imports lead, the rest follow in ordinal order, and aliases stay last, which is the
    // order the shared editor configuration asks for and keeps the insertion point deterministic.
    private static int FindIndex(SyntaxList<UsingDirectiveSyntax> usings, string namespaceName)
    {
        bool imported = IsSystem(namespaceName);
        int index = 0;

        for (int position = 0; position < usings.Count; position++)
        {
            UsingDirectiveSyntax existing = usings[position];

            if (existing.Alias is not null)
                break;

            string name = existing.Name?.ToString() ?? string.Empty;
            bool system = IsSystem(name);

            if (system != imported)
            {
                if (imported)
                    break;

                index = position + 1;

                continue;
            }

            if (string.CompareOrdinal(name, namespaceName) >= 0)
                break;

            index = position + 1;
        }

        return index;
    }

    private static SyntaxTrivia GetLineEnding(CompilationUnitSyntax unit)
    {
        foreach (SyntaxTrivia trivia in unit.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                return trivia;
        }

        return SyntaxFactory.LineFeed;
    }

    private static async Task<Document> Import(Document document, TextSpan diagnosticSpan, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (root is not CompilationUnitSyntax unit || model is null)
            return document;

        ExpressionSyntax? reference = Find(root, diagnosticSpan);
        QualifiedTypeImport.ImportResolution? resolution = reference is null ? null : QualifiedTypeImport.Resolve(reference, model, cancellationToken);

        if (reference is null || resolution is null || resolution.Collides || Shorten(reference) is not SimpleNameSyntax shortName)
            return document;

        CompilationUnitSyntax shortened = unit.ReplaceNode(reference, shortName);

        return !resolution.Import
            ? document.WithSyntaxRoot(shortened)
            : document.WithSyntaxRoot(AddImport(shortened, resolution.Container.ToDisplayString(), GetLineEnding(unit)));
    }

    private static async Task<Document> ImportAll(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (root is not CompilationUnitSyntax unit || model is null)
            return document;

        HashSet<ExpressionSyntax> references = [];

        // AddImport skips a namespace already present, so repeats here are harmless and the
        // sorted insertion keeps the result identical whatever order they arrive in.
        List<string> imports = [];

        foreach (Diagnostic diagnostic in diagnostics)
        {
            ExpressionSyntax? reference = Find(root, diagnostic.Location.SourceSpan);
            QualifiedTypeImport.ImportResolution? resolution = reference is null ? null : QualifiedTypeImport.Resolve(reference, model, cancellationToken);

            if (reference is null || resolution is null || resolution.Collides || !references.Add(reference))
                continue;

            if (resolution.Import)
                imports.Add(resolution.Container.ToDisplayString());
        }

        if (references.Count == 0)
            return document;

        // A reference can hold another, such as a type argument, so each shortens what is left of it
        // after the ones inside it were shortened.
        CompilationUnitSyntax rewritten = unit.ReplaceNodes(references, static (original, current) => Shorten(current) ?? current);
        SyntaxTrivia lineEnding = GetLineEnding(unit);

        foreach (string imported in imports)
            rewritten = AddImport(rewritten, imported, lineEnding);

        return document.WithSyntaxRoot(rewritten);
    }

    private static bool IsFixable(SyntaxNode root, SemanticModel model, TextSpan diagnosticSpan, CancellationToken cancellationToken)
    {
        QualifiedTypeImport.ImportResolution? resolution = Find(root, diagnosticSpan) is not ExpressionSyntax reference ? null : QualifiedTypeImport.Resolve(reference, model, cancellationToken);

        return resolution is not null && !resolution.Collides;
    }

    private static bool IsSystem(string namespaceName)
    {
        return string.Equals(namespaceName, b: "System", StringComparison.Ordinal) || namespaceName.StartsWith(value: "System.", StringComparison.Ordinal);
    }

    // The short name already spells the type, including any type arguments.
    private static SimpleNameSyntax? Shorten(SyntaxNode reference)
    {
        return QualifiedTypeImport.GetShortName(reference)?.WithTriviaFrom(reference);
    }
}
