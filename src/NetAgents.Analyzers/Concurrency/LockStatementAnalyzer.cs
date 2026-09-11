using System;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.Analyzers.Concurrency;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LockStatementAnalyzer : DiagnosticAnalyzer
{
    public const string RuleIdentifier = "NETAGENTS0003";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Lock statements are forbidden",
        messageFormat: "Prefer immutable state, atomics, channels, or Nito.AsyncEx when coordination is unavoidable",
        category: "Concurrency",
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
        context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.LockStatement);
    }

    private static void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }
}
