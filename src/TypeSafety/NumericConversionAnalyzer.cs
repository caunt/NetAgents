using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.TypeSafety;

/// <summary>
/// Rejects numeric casts, implicit numeric conversions, and compound assignments that narrow their result
/// in favor of the generic math factories, which say whether a value out of range throws, saturates, or truncates.
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
        messageFormat: "Convert '{0}' to '{1}' with {1}.CreateChecked, CreateSaturating, or CreateTruncating instead of {2}",
        category: "TypeSafety",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
        context.RegisterOperationAction(AnalyzeCompoundAssignment, OperationKind.CompoundAssignment);
    }

    // A compound assignment on a byte, sbyte, short, or ushort target computes in int and narrows the result
    // back without a word, wrapping a value out of range: 250 += 10 leaves 4.
    private static void AnalyzeCompoundAssignment(OperationAnalysisContext context)
    {
        ICompoundAssignmentOperation compound = (ICompoundAssignmentOperation)context.Operation;
        SemanticModel? model = compound.SemanticModel;
        CompoundNarrowing? narrowing = model is null ? null : NumericConversion.GetCompoundNarrowing(compound);

        if (model is null || narrowing is null)
            return;

        int position = compound.Syntax.SpanStart;
        string source = model.Compilation.GetSpecialType(SpecialType.System_Int32).ToMinimalDisplayString(model, position);
        string target = narrowing.Target.ToMinimalDisplayString(model, position);

        // The fix rewrites the assignment only where reading the target twice reads the same thing.
        ImmutableDictionary<string, string?> properties = narrowing.Rewritable
            ? []
            : ImmutableDictionary<string, string?>.Empty.Add(NumericConversion.ManualProperty, value: "true");

        string written = narrowing.Rewritable
            ? "a compound assignment that narrows its result"
            : $"a compound assignment that narrows its result; write it as a plain assignment such as 'x = {target}.{narrowing.Factory}(x op y)', "
                + "moving any index or receiver with side effects into a local first so it is evaluated once";

        context.ReportDiagnostic(Diagnostic.Create(Rule, compound.Syntax.GetLocation(), properties, source, target, written));
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

        context.ReportDiagnostic(Diagnostic.Create(Rule, conversion.Syntax.GetLocation(), messageArgs: [source, target, "a numeric cast or implicit conversion"]));
    }
}
