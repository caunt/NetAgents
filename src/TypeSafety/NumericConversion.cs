using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace NetAgents.Analyzers.TypeSafety;

internal static class NumericConversion
{
    public const string RuleIdentifier = "NETAGENTS0032";

    // The factory that keeps a numeric conversion's behavior, or nothing for a conversion the rule leaves
    // alone. An implicit conversion cannot overflow and a checked or decimal one throws when it would, so
    // both keep their behavior through CreateChecked; any other cast wraps or truncates as
    // CreateTruncating does, saturating a floating-point value as casts do since .NET 9.
    public static string? GetFactory(IConversionOperation conversion, SemanticModel model, CancellationToken cancellationToken)
    {
        CommonConversion kind = conversion.Conversion;

        if (!kind.IsNumeric || kind.IsUserDefined || kind.IsNullable)
            return null;

        // A constant is converted, and checked, by the compiler, which also needs it to stay constant.
        if (IsConstant(conversion.Operand))
            return null;

        if (conversion.Operand.Type is not ITypeSymbol source || conversion.Type is not ITypeSymbol target)
            return null;

        // A char is a UTF-16 code unit rather than a quantity, and its factories are not public members.
        if (source.SpecialType is SpecialType.System_Char || target.SpecialType is SpecialType.System_Char || !HasFactory(target))
            return null;

        bool written = !conversion.IsImplicit;

        if (written ? conversion.Syntax is not CastExpressionSyntax : conversion.Syntax is not ExpressionSyntax)
            return null;

        if (IsInsideExpressionTree(conversion.Syntax, model, cancellationToken))
            return null;

        bool decimals = source.SpecialType is SpecialType.System_Decimal || target.SpecialType is SpecialType.System_Decimal;

        return kind.IsImplicit || conversion.IsChecked || decimals ? "CreateChecked" : "CreateTruncating";
    }

    // Generic math gives every numeric type public CreateChecked, CreateSaturating and CreateTruncating
    // methods; a target framework without them has nothing to convert with, so the rule stays silent.
    private static bool HasFactory(ITypeSymbol target)
    {
        foreach (ISymbol member in target.GetMembers(name: "CreateChecked"))
        {
            if (member is IMethodSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public })
                return true;
        }

        return false;
    }

    // A constant expression as C# defines one for numbers: literals, constants, and the operators and
    // conversions the compiler folds when every operand is itself constant.
    private static bool IsConstant(IOperation operation)
    {
        return operation switch
        {
            ILiteralOperation or IDefaultValueOperation or ISizeOfOperation => true,
            IFieldReferenceOperation reference => reference.Field.IsConst,
            ILocalReferenceOperation reference => reference.Local.IsConst,
            IConversionOperation conversion => IsConstant(conversion.Operand),
            IUnaryOperation unary => IsConstant(unary.Operand),
            IBinaryOperation binary => IsConstant(binary.LeftOperand) && IsConstant(binary.RightOperand),
            IConditionalOperation { WhenFalse: IOperation otherwise } conditional => IsConstant(conditional.Condition) && IsConstant(conditional.WhenTrue) && IsConstant(otherwise),
            _ => false,
        };
    }

    private static bool IsInsideExpressionTree(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (SyntaxNode ancestor in node.Ancestors())
        {
            if (ancestor is not AnonymousFunctionExpressionSyntax function)
                continue;

            ITypeSymbol? converted = model.GetTypeInfo(function, cancellationToken).ConvertedType;

            if (converted?.ContainingNamespace?.ToDisplayString() is "System.Linq.Expressions")
                return true;
        }

        return false;
    }
}
