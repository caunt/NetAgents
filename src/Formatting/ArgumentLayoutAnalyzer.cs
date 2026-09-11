using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting;

/// <summary>
/// Requires compact argument and parameter lists up to the shared limit, and one item per line above it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ArgumentLayoutAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = ArgumentLayout.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Argument and parameter lists must match their length",
        messageFormat: "Keep argument and parameter lists on one line up to {0} characters; otherwise put each item and the closing delimiter on separate lines",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(
            AnalyzeList,
            SyntaxKind.ArgumentList,
            SyntaxKind.BracketedArgumentList,
            SyntaxKind.AttributeArgumentList,
            SyntaxKind.ParameterList,
            SyntaxKind.BracketedParameterList
        );
    }

    private static void AnalyzeList(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.ContainsDiagnostics || context.Node.ContainsDirectives)
            return;

        SourceText source = context.Node.SyntaxTree.GetText(context.CancellationToken);

        if (!ArgumentLayout.HasExpectedLayout(context.Node, source))
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), LayoutLimits.MaximumInlineLength.ToString(CultureInfo.InvariantCulture)));
    }
}
