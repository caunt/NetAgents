using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.TypeSafety;

/// <summary>
/// Rejects conversions that box value types into reference types.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BoxingConversionAnalyzer() : PolicyAnalyzer(Rule, analyzesRazorCode: true)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0001";

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Boxing conversions are forbidden",
        messageFormat: "Keep this value typed instead of boxing it: call a generic or overloaded API that takes the value's own type, format it with ToString or string.Create, or keep it in a typed collection. Casting it to object or to an interface boxes it just the same." + PolicyMessages.ChangeTheCode,
        category: "TypeSafety",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
    }

    private static void AnalyzeConversion(OperationAnalysisContext context)
    {
        IConversionOperation conversion = (IConversionOperation)context.Operation;

        if (conversion.Operand.Type is null || conversion.Type is null)
            return;

        CSharpCompilation compilation = (CSharpCompilation)context.Compilation;

        if (compilation.ClassifyConversion(conversion.Operand.Type, conversion.Type).IsBoxing)
            context.ReportDiagnostic(Diagnostic.Create(Rule, conversion.Syntax.GetLocation()));
    }
}
