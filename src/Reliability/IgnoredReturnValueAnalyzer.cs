using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Reliability;

/// <summary>
/// Requires returned values to be consumed instead of ignored or assigned to a discard.
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
        title: "Return values must be consumed",
        messageFormat: "Use the returned value in a condition, assignment, argument, or return instead of ignoring or discarding it",
        category: "Reliability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterOperationAction(AnalyzeExpressionStatement, OperationKind.ExpressionStatement);
        context.RegisterOperationAction(AnalyzeAssignment, OperationKind.SimpleAssignment, OperationKind.DeconstructionAssignment);
    }

    private static void AnalyzeExpressionStatement(OperationAnalysisContext context)
    {
        IOperation expression = ((IExpressionStatementOperation)context.Operation).Operation;

        if (ReturnsValue(expression))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, expression.Syntax.GetLocation()));
        }
    }

    private static void AnalyzeAssignment(OperationAnalysisContext context)
    {
        IAssignmentOperation assignment = (IAssignmentOperation)context.Operation;

        if (ContainsDiscard(assignment.Target))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, assignment.Syntax.GetLocation()));
        }
    }

    private static bool ContainsDiscard(IOperation target)
    {
        return target switch
        {
            IDiscardOperation => true,
            IDeclarationExpressionOperation declaration => ContainsDiscard(declaration.Expression),
            ITupleOperation tuple => tuple.Elements.Any(ContainsDiscard),
            _ => false,
        };
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
