using System;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.SourceFiles;

/// <summary>
/// Requires each top-level type to occupy its own matching source file.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SourceFileStructureAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0006";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Each top-level type needs its own matching file",
        messageFormat: "Keep one top-level type per file and match the file name to the type name",
        category: "Structure",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(
            AnalyzeDeclaration,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.InterfaceDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration,
            SyntaxKind.EnumDeclaration,
            SyntaxKind.DelegateDeclaration
        );
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.Ancestors().Any(static ancestor => ancestor is BaseTypeDeclarationSyntax))
            return;

        SyntaxToken identifier = context.Node is BaseTypeDeclarationSyntax typeDeclaration
            ? typeDeclaration.Identifier
            : ((DelegateDeclarationSyntax)context.Node).Identifier;

        string fileName = Path.GetFileNameWithoutExtension(context.Node.SyntaxTree.FilePath);
        SyntaxNode parent = context.Node.SyntaxTree.GetRoot(context.CancellationToken);

        int declarationCount = parent.DescendantNodes(static descendant => descendant is not BaseTypeDeclarationSyntax)
            .Count(static descendant => descendant is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);

        if (!string.Equals(fileName, identifier.ValueText, StringComparison.Ordinal) || declarationCount > 1)
            context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation()));
    }
}
