using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting;

/// <summary>
/// Requires braces for multiline bodies and omits optional braces for single-line bodies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ControlFlowBracesAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = ControlFlowBraces.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Braces must match the body layout",
        messageFormat: "Use braces for multiline bodies and omit optional braces for single-line bodies, keeping conditional chains consistent",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeBraces, SyntaxKind.CompilationUnit);
    }

    private static void AnalyzeBraces(SyntaxNodeAnalysisContext context)
    {
        SourceText source = context.Node.SyntaxTree.GetText(context.CancellationToken);

        foreach (KeyValuePair<TextSpan, SyntaxNode> change in ControlFlowBraces.GetChanges(context.Node, source))
            context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Node.SyntaxTree, change.Key)));
    }
}
