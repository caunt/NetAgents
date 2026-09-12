using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Ordering;

/// <summary>
/// Requires consistent member ordering within type declarations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MemberOrderingAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = MemberOrdering.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Type members must follow the standard order",
        messageFormat: "Order members by kind, visibility, storage, readonly modifier, and name",
        category: "Ordering",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(
            AnalyzeOrder,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.InterfaceDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration
        );
    }

    private static void AnalyzeOrder(SyntaxNodeAnalysisContext context)
    {
        TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.Node;

        if (!MemberOrdering.IsOrdered(declaration))
            context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation()));
    }
}
