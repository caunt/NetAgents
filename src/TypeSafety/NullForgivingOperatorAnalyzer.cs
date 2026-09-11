using System;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.Analyzers.TypeSafety;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullForgivingOperatorAnalyzer : DiagnosticAnalyzer
{
    public const string RuleIdentifier = "NETAGENTS0007";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Null-forgiving operators are forbidden",
        messageFormat: "Prove non-null state through validation, nullable annotations, or control flow",
        category: "TypeSafety",
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
        context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.SuppressNullableWarningExpression);
    }

    private static void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }
}