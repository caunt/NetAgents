using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Requires a type to be imported and named directly instead of written with its namespace.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QualifiedTypeImportAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = QualifiedTypeImport.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Types are imported rather than qualified",
        messageFormat: "Import this type and name it directly instead of qualifying it{0}",
        category: "Readability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeQualifiedName, SyntaxKind.QualifiedName);
    }

    private static void AnalyzeQualifiedName(SyntaxNodeAnalysisContext context)
    {
        QualifiedNameSyntax name = (QualifiedNameSyntax)context.Node;

        QualifiedTypeImport.ImportResolution? resolution = QualifiedTypeImport.Resolve(name, context.SemanticModel, context.CancellationToken);

        if (resolution is null)
            return;

        // A colliding name cannot be imported plainly, and choosing the alias is the author's call.
        string hint = resolution.Collides ? ", under a using alias because this name already refers to another type" : string.Empty;

        context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), hint));
    }
}
