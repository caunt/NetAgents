using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting.Clauses;

/// <summary>
/// Requires the body of an expression-bodied lambda to start on the line of its <c>=&gt;</c> when its first line fits
/// there.
/// </summary>
/// <remarks>
/// <c>async () =&gt;</c> followed by its body on the next line leaves that line at whatever indentation it was written
/// with, since no formatter places it. The fix joins the body to the arrow; the body's own chain or argument list then
/// lays out from that line.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LambdaBodyPlacementAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = LambdaBodyPlacement.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "A lambda's expression body must start on the line of its arrow",
        messageFormat: "Start this lambda's body on the line of its =>: its first line fits there within 128 characters, and no formatter indents a body left on the next line, so write value => value.Length and let a long body break inside itself, at its chain or its argument list",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression);
    }

    private static void AnalyzeLambda(SyntaxNodeAnalysisContext context)
    {
        LambdaExpressionSyntax lambda = (LambdaExpressionSyntax)context.Node;

        if (LambdaBodyPlacement.IsDetached(lambda, lambda.SyntaxTree.GetText(context.CancellationToken)))
            context.ReportDiagnostic(Diagnostic.Create(Rule, lambda.ArrowToken.GetLocation()));
    }
}
