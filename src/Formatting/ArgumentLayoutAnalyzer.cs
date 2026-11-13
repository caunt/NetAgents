using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;
using NetAgents.Analyzers.Formatting.Collections;

namespace NetAgents.Analyzers.Formatting;

/// <summary>
/// Requires compact argument and parameter lists, collection expressions, and initializers up to the shared limit, and
/// one item per line above it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ArgumentLayoutAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = ArgumentLayout.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Argument and parameter lists must match their length",
        messageFormat: "Keep argument and parameter lists, collection expressions, and initializers on one line when their contents fit within {0} characters; otherwise put each item on its own line: in a list or collection expression every item after the first, closing right after the last, and in an initializer every element, with the braces on lines of their own",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeList,
            SyntaxKind.ArgumentList,
            SyntaxKind.BracketedArgumentList,
            SyntaxKind.AttributeArgumentList,
            SyntaxKind.ParameterList,
            SyntaxKind.BracketedParameterList,
            SyntaxKind.CollectionExpression,
            SyntaxKind.ObjectInitializerExpression,
            SyntaxKind.CollectionInitializerExpression,
            SyntaxKind.ArrayInitializerExpression,
            SyntaxKind.ComplexElementInitializerExpression,
            SyntaxKind.WithInitializerExpression);
    }

    private static void AnalyzeList(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.ContainsDiagnostics || context.Node.ContainsDirectives)
            return;

        SourceText source = context.Node.SyntaxTree.GetText(context.CancellationToken);

        // Collection expressions and initializers of two or more elements take the same one-line-or-one-per-line layout.
        bool settled = ArgumentLayout.IsList(context.Node)
            ? ArgumentLayout.HasExpectedLayout(context.Node, source)
            : !CollectionLayout.CanLayOut(context.Node) || CollectionLayout.HasExpectedLayout(context.Node, source);

        if (!settled)
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), LayoutLimits.MaximumInlineLength.ToString(CultureInfo.InvariantCulture)));
    }
}
