using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Requires a <c>try</c> statement that only wraps another one for its <c>finally</c> to be merged with it.
/// </summary>
/// <remarks>
/// A <c>try</c> whose block holds nothing but a <c>try</c> with handlers, and which adds only a <c>finally</c>, is one
/// statement written as two: <c>try { … } catch (…) { … } finally { … }</c> runs the same code in the same order. The fix
/// merges them.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NestedTryAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = NestedTry.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "A try that only adds a finally must be merged with the try it wraps",
        messageFormat: "Merge these two try statements into one: the outer one only adds a finally to the try it wraps, so write try { … } catch (…) { … } finally { … }, which runs the same code in the same order",
        category: "Readability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeTry, SyntaxKind.TryStatement);
    }

    private static void AnalyzeTry(SyntaxNodeAnalysisContext context)
    {
        TryStatementSyntax outer = (TryStatementSyntax)context.Node;

        if (NestedTry.GetMergeable(outer) is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, outer.TryKeyword.GetLocation()));
    }
}
