using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting.Members;

namespace NetAgents.Analyzers.CodeFixes.Formatting;

/// <summary>
/// Joins a member wrapped at its <c>=</c> or <c>=&gt;</c> back onto one line, one space on each side of the operator.
/// </summary>
/// <remarks>
/// The fix is offered only where the analyzer's check still holds against the current text, so it never joins a
/// member that would end up longer than the layout limit.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(WrappedMemberCodeFixProvider))]
[Shared]
public sealed class WrappedMemberCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [WrappedMember.RuleIdentifier];

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

        if (root is null)
            return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            SyntaxToken operatorToken = root.FindToken(diagnostic.Location.SourceSpan.Start);
            MemberDeclarationSyntax? member = operatorToken.Parent?.FirstAncestorOrSelf<MemberDeclarationSyntax>();

            if (member is null || WrappedMember.GetOperator(member) != operatorToken || !WrappedMember.IsWrapped(member, operatorToken, text))
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Join the member onto one line",
                    cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(WrappedMember.Join(root, operatorToken))),
                    nameof(WrappedMemberCodeFixProvider)),
                diagnostic);
        }
    }
}
