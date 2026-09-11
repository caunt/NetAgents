using System;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace NetAgents.Analyzers.TypeSafety;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BoxingConversionAnalyzer : DiagnosticAnalyzer
{
    public const string RuleIdentifier = "NETAGENTS0001";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Boxing conversions are forbidden",
        messageFormat: "Use a strongly typed or constrained generic operation instead of boxing a value",
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
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
    }

    private static void AnalyzeConversion(OperationAnalysisContext context)
    {
        IConversionOperation conversion = (IConversionOperation)context.Operation;
        if (conversion.Operand.Type is null || conversion.Type is null)
        {
            return;
        }

        CSharpCompilation compilation = (CSharpCompilation)context.Compilation;
        if (compilation.ClassifyConversion(conversion.Operand.Type, conversion.Type).IsBoxing)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, conversion.Syntax.GetLocation()));
        }
    }
}
