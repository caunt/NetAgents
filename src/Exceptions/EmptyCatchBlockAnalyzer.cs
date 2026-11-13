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
public sealed class EmptyCatchBlockAnalyzer() : PolicyAnalyzer(Rule, analyzesRazorCode: true)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0004";

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Catch blocks must handle exceptions",
        messageFormat: "Do something with the caught exception: recover in a way the code shows, log it with the context a reader needs, or rethrow it with throw, and catch only the types this code expects. A comment, an empty statement, or a discarded variable does not handle it." + PolicyMessages.ChangeTheCode,
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
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }
}
