using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Concurrency;

/// <summary>
/// Rejects lock statements in favor of asynchronous or atomic coordination.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LockStatementAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0003";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Lock statements are forbidden",
        messageFormat: "Prefer immutable state, atomics, channels, or Nito.AsyncEx when coordination is unavoidable",
        category: "Concurrency",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.LockStatement);
    }

    private static void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }
}
