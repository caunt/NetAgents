using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace NetAgents.Analyzers.TypeSafety;

internal static class NumericConversion
{
    // Marks a narrowing compound assignment the fix leaves to its author, because rewriting it would
    // evaluate the target twice.
    public const string ManualProperty = "Manual";

    public const string RuleIdentifier = "NETAGENTS0032";

    // The narrowing a compound assignment on a byte, sbyte, short, or ushort target performs on its result,
    // or nothing for one that narrows nothing. C# computes such an assignment in int and converts the result
    // back without saying so, which wraps an out-of-range value just as an unchecked cast would; inside a
    // checked context it throws instead.
    public static CompoundNarrowing? GetCompoundNarrowing(ICompoundAssignmentOperation compound)
    {
        CommonConversion narrowing = compound.OutConversion;

        if (compound.OperatorMethod is not null || compound.IsLifted || !narrowing.IsNumeric || narrowing.IsImplicit)
            return null;

        if (compound.Target.Type is not ITypeSymbol target || !IsNarrowerThanInt(target) || !HasFactory(target))
            return null;

        bool widensValue = compound.Value is IConversionOperation { IsImplicit: true, Conversion.IsNumeric: true } conversion
            && !IsConstant(conversion.Operand)
            && conversion.Operand.Type?.SpecialType is not SpecialType.System_Char;

        string factory = compound.IsChecked ? "CreateChecked" : "CreateTruncating";

        return new CompoundNarrowing(target, factory, widensValue, IsRereadable(compound.Target));
    }

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

        if (IsWidenedCompoundOperand(conversion))
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

    private static bool IsNarrowerThanInt(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16;
    }

    // A target the fix may read a second time: a local, a parameter, or a field reached only through
    // locals, parameters, fields, or this. An indexer, a property, or a call could do something new the
    // second time, which the compound form never does.
    private static bool IsRereadable(IOperation target)
    {
        return target switch
        {
            ILocalReferenceOperation or IParameterReferenceOperation or IInstanceReferenceOperation => true,
            IFieldReferenceOperation field => field.Instance is null || IsRereadable(field.Instance),
            _ => false,
        };
    }

    // The operand a compound assignment widens for its operator cannot be written widened while the
    // assignment keeps its compound form, which would then fail to compile; the narrowing of the
    // assignment's result is reported in its place. An operand converted to the target's own type, as in
    // long += int, can be written that way and is still reported.
    private static bool IsWidenedCompoundOperand(IConversionOperation conversion)
    {
        return conversion.Parent is ICompoundAssignmentOperation compound && compound.Value == conversion && !SymbolEqualityComparer.Default.Equals(conversion.Type, compound.Target.Type);
    }
}
