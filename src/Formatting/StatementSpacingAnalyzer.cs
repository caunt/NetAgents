using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting;

/// <summary>
/// Separates control flow and multiline variable declarations from adjacent statements.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatementSpacingAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = StatementSpacing.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Statements require blank lines",
        messageFormat: "Separate control-flow statements and multiline variable declarations from adjacent statements with a blank line",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeSpacing, SyntaxKind.CompilationUnit);
    }

    private static void AnalyzeSpacing(SyntaxNodeAnalysisContext context)
    {
        SourceText source = context.Node.SyntaxTree.GetText(context.CancellationToken);

        foreach (KeyValuePair<TextSpan, TextChange> change in StatementSpacing.GetChanges(context.Node, source))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Node.SyntaxTree, change.Key)));
        }
    }
}
