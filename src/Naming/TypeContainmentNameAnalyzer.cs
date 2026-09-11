using System;
using System.IO;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Naming;

/// <summary>
/// Rejects type names that duplicate their owning namespace or feature directory.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypeContainmentNameAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0011";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Type names must differ from containing segments",
        messageFormat: "Choose a type name that differs from its namespace and containing feature directories",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration,
            SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration,
            SyntaxKind.RecordDeclaration, SyntaxKind.RecordStructDeclaration, SyntaxKind.EnumDeclaration,
            SyntaxKind.DelegateDeclaration);
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not INamedTypeSymbol type)
            return;

        INamespaceSymbol containingNamespace = type.ContainingNamespace;

        while (!containingNamespace.IsGlobalNamespace)
        {
            if (string.Equals(type.Name, containingNamespace.Name, StringComparison.OrdinalIgnoreCase))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));

                return;
            }

            containingNamespace = containingNamespace.ContainingNamespace;
        }

        // Only directories below the project root are source architecture, not checkout locations.
        bool hasProjectDirectory = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
            key: "build_property.MSBuildProjectDirectory", out string? projectDirectory);

        if (!hasProjectDirectory || string.IsNullOrWhiteSpace(projectDirectory))
            return;

        string root = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(context.Node.SyntaxTree.FilePath)) ?? root;

        if (!sourceDirectory.StartsWith(root + Path.DirectorySeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase))
            return;

        string relativeDirectory = sourceDirectory.Substring(root.Length + 1);

        foreach (string segment in relativeDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.Equals(type.Name, segment, StringComparison.OrdinalIgnoreCase))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));

                return;
            }
        }
    }
}
