using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.TypeSafety;

/// <summary>
/// Rejects numeric casts such as <c>(int)value</c> in favor of the generic math factories, which say whether
/// a value out of range throws, saturates, or truncates. Implicit conversions are the language's own and are
/// left alone.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NumericConversionAnalyzer() : PolicyAnalyzer(Rule, analyzesRazorCode: true)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = NumericConversion.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Numeric conversions must name their overflow behavior",
        messageFormat: "Convert '{0}' to '{1}' with {1}.CreateChecked when an out-of-range value is a bug, {1}.CreateSaturating when it should clamp to the nearest limit, or {1}.CreateTruncating when wrapping or dropping the fraction is intended, instead of a numeric cast",
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
        SemanticModel? model = conversion.SemanticModel;

        if (model is null || NumericConversion.GetFactory(conversion, model, context.CancellationToken) is null)
            return;

        int position = conversion.Syntax.SpanStart;
        string source = conversion.Operand.Type?.ToMinimalDisplayString(model, position) ?? string.Empty;
        string target = conversion.Type?.ToMinimalDisplayString(model, position) ?? string.Empty;

        context.ReportDiagnostic(Diagnostic.Create(Rule, conversion.Syntax.GetLocation(), source, target));
    }
}
