using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Finds a delegate constructed around a lambda, anonymous method, or method group where the target type already
/// makes the conversion.
/// </summary>
/// <remarks>
/// <para>
/// <c>ConsoleCancelEventHandler handler = new((sender, eventArguments) => …);</c> says
/// <c>ConsoleCancelEventHandler handler = (sender, eventArguments) => …;</c> with an extra wrapper, and so does
/// <c>Changed += new EventHandler(OnChanged);</c> for <c>Changed += OnChanged;</c>.
/// </para>
/// <para>
/// Only places that write the delegate type out are covered: a declaration with an explicit type, a property
/// initializer, and an assignment, <c>+=</c>, or <c>-=</c> whose target has the delegate's type. Elsewhere the
/// lambda's own type or an overload could change, so <c>var</c> declarations and arguments are left alone, and so is a
/// delegate built from another delegate instance, which the constructor wraps rather than converts.
/// </para>
/// </remarks>
internal static class DelegateCreation
{
    public const string RuleIdentifier = "NETAGENTS0052";

    // The lambda, anonymous method, or method group the creation only wraps, or nothing when it cannot stand in its
    // place.
    public static ExpressionSyntax? GetWrapped(BaseObjectCreationExpressionSyntax creation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (creation.Initializer is not null || creation.ArgumentList is not { Arguments.Count: 1 } arguments)
            return null;

        ArgumentSyntax argument = arguments.Arguments[index: 0];

        if (argument.NameColon is not null || !argument.RefKindKeyword.IsKind(SyntaxKind.None))
            return null;

        // A lambda, an anonymous method, or a method group converts to the delegate; another delegate instance does not.
        ExpressionSyntax wrapped = argument.Expression;

        bool convertible = wrapped is AnonymousFunctionExpressionSyntax || model.GetSymbolInfo(wrapped, cancellationToken)
            .Symbol is IMethodSymbol;

        ITypeSymbol? type = model.GetTypeInfo(creation, cancellationToken).Type;

        bool sameType = type is INamedTypeSymbol { TypeKind: TypeKind.Delegate } && SymbolEqualityComparer.Default
            .Equals(GetTargetType(creation, model, cancellationToken), type);

        return convertible && sameType ? wrapped : null;
    }

    private static bool AssignsDelegate(AssignmentExpressionSyntax assignment)
    {
        return assignment.Kind() is SyntaxKind.SimpleAssignmentExpression or SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression;
    }

    // The type the place of the creation writes out, which a lambda or method group there converts to.
    private static ITypeSymbol? GetTargetType(BaseObjectCreationExpressionSyntax creation, SemanticModel model, CancellationToken cancellationToken)
    {
        return creation.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } when !declaration.Type.IsVar
                => model.GetTypeInfo(declaration.Type, cancellationToken).Type,
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } => model.GetTypeInfo(property.Type, cancellationToken)
                .Type,
            AssignmentExpressionSyntax assignment when assignment.Right == creation && AssignsDelegate(assignment)
                => model.GetTypeInfo(assignment.Left, cancellationToken).Type,
            _ => null,
        };
    }
}
