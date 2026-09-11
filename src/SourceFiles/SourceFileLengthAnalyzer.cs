using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.SourceFiles;

/// <summary>
/// Limits authored source files to fewer than one thousand lines.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SourceFileLengthAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0010";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Source files must contain fewer than one thousand lines",
        messageFormat: "Split this file by responsibility; authored files may contain at most 999 lines",
        category: "Structure",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        if (context.Tree.GetText(context.CancellationToken).Lines.Count >= 1_000)
            context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Tree, new TextSpan(start: 0, length: 0))));
    }
}
