using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Reliability;

/// <summary>
/// Requires returned values to be consumed and rejects underscore assignments and loop discards.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoredReturnValueAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0015";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Values must be consumed instead of discarded",
        messageFormat: "Consume returned values and use descriptive names instead of assigning values to '_' or discarding deconstructed values",
        category: "Reliability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterOperationAction(AnalyzeExpressionStatement, OperationKind.ExpressionStatement);
        context.RegisterOperationAction(
            AnalyzeAssignment,
            OperationKind.SimpleAssignment,
            OperationKind.DeconstructionAssignment,
            OperationKind.CompoundAssignment,
            OperationKind.CoalesceAssignment
        );
        context.RegisterOperationAction(AnalyzeVariable, OperationKind.VariableDeclarator);
        context.RegisterOperationAction(AnalyzeLoop, OperationKind.Loop);
    }

    private static void AnalyzeExpressionStatement(OperationAnalysisContext context)
    {
        IOperation expression = ((IExpressionStatementOperation)context.Operation).Operation;

        if (ReturnsValue(expression))
            context.ReportDiagnostic(Diagnostic.Create(Rule, expression.Syntax.GetLocation()));
    }

    private static void AnalyzeAssignment(OperationAnalysisContext context)
    {
        IAssignmentOperation assignment = (IAssignmentOperation)context.Operation;

        if (ContainsDiscard(assignment.Target))
            context.ReportDiagnostic(Diagnostic.Create(Rule, assignment.Syntax.GetLocation()));
    }

    private static bool ContainsDiscard(IOperation target)
    {
        return target switch
        {
            IDiscardOperation or ILocalReferenceOperation { Local.Name: "_" } or IParameterReferenceOperation { Parameter.Name: "_" }
                or IFieldReferenceOperation { Field.Name: "_" } or IPropertyReferenceOperation { Property.Name: "_" }
                or IVariableDeclaratorOperation { Symbol.Name: "_" } => true,
            IParenthesizedOperation parenthesized => ContainsDiscard(parenthesized.Operand),
            IDeclarationExpressionOperation declaration => ContainsDiscard(declaration.Expression),
            ITupleOperation tuple => tuple.Elements.Any(ContainsDiscard),
            _ => false,
        };
    }

    private static void AnalyzeVariable(OperationAnalysisContext context)
    {
        if (context.Operation is IVariableDeclaratorOperation { Symbol.Name: "_", Initializer: not null } declaration)
            context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Syntax.GetLocation()));
    }

    private static void AnalyzeLoop(OperationAnalysisContext context)
    {
        if (context.Operation is IForEachLoopOperation loop && ContainsDiscard(loop.LoopControlVariable))
            context.ReportDiagnostic(Diagnostic.Create(Rule, loop.LoopControlVariable.Syntax.GetLocation()));
    }

    private static bool ReturnsValue(IOperation expression)
    {
        return expression switch
        {
            IConditionalAccessOperation conditionalAccess => ReturnsValue(conditionalAccess.WhenNotNull),
            IParenthesizedOperation parenthesized => ReturnsValue(parenthesized.Operand),
            IConversionOperation conversion => ReturnsValue(conversion.Operand),
            IInvocationOperation or IFunctionPointerInvocationOperation or IAwaitOperation =>
                expression.Type is { SpecialType: not SpecialType.System_Void, TypeKind: not TypeKind.Error },
            _ => false,
        };
    }
}
