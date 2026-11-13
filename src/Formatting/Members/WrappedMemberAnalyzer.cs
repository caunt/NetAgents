using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting.Members;

/// <summary>
/// Joins a member wrapped at the <c>=</c> of its initializer or the <c>=&gt;</c> of its expression body when the whole
/// member fits within 128 characters on one line.
/// </summary>
/// <remarks>
/// <code>
/// public Memory&lt;byte&gt; LocalPublicKey { get; init; } =
///     NaClV3Cryptography.CryptoScalarMultBase(localPrivateKey.Span);
/// </code>
/// <para>
/// becomes one line, since it fits. Fields, properties, events, methods, constructors, operators, and indexers are
/// covered. A member that spans lines anywhere besides the operator is left to the rules for its argument lists,
/// initializers, and lambdas, and a comment beside the operator keeps it from being joined.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WrappedMemberAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = WrappedMember.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "A member that fits on one line must be written on one line",
        messageFormat: "Join this member onto one line: it fits within 128 characters, so the line break around '{0}' only splits a short declaration",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeMember,
            SyntaxKind.FieldDeclaration,
            SyntaxKind.EventFieldDeclaration,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.DestructorDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.IndexerDeclaration);
    }

    private static void AnalyzeMember(SyntaxNodeAnalysisContext context)
    {
        MemberDeclarationSyntax member = (MemberDeclarationSyntax)context.Node;
        SyntaxToken operatorToken = WrappedMember.GetOperator(member);

        if (WrappedMember.IsWrapped(member, operatorToken, member.SyntaxTree.GetText(context.CancellationToken)))
            context.ReportDiagnostic(Diagnostic.Create(Rule, operatorToken.GetLocation(), operatorToken.Text));
    }
}
