using System;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.SourceFiles;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SourceFileLengthAnalyzer : DiagnosticAnalyzer
{
    public const string RuleIdentifier = "NETAGENTS0010";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Source files must contain fewer than one thousand lines",
        messageFormat: "Split this file by responsibility; authored files may contain at most 999 lines",
        category: "Structure",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        if (context.Tree.GetText(context.CancellationToken).Lines.Count >= 1_000)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule,
                Location.Create(context.Tree, new TextSpan(start: 0, length: 0))));
        }
    }
}
