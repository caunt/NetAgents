using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Exceptions;

/// <summary>
/// Requires catch blocks to handle or rethrow their exceptions.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EmptyCatchBlockAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0004";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Catch blocks must handle exceptions",
        messageFormat: "Handle, log, or explicitly rethrow the caught exception",
        category: "Exceptions",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.CatchClause);
    }

    private static void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
    {
        CatchClauseSyntax catchClause = (CatchClauseSyntax)context.Node;

        if (catchClause.Block.Statements.Count > 0)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }
}
