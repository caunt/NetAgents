using System;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Design;

/// <summary>
/// Requires authored result contracts to use dedicated named types instead of tuples.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TupleReturnTypeAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0024";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Tuple return types are forbidden",
        messageFormat: "Return a dedicated named record, class, or struct instead of a tuple",
        category: "Design",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(
            AnalyzeDeclaration,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.IndexerDeclaration,
            SyntaxKind.DelegateDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration
        );
        context.RegisterSyntaxNodeAction(AnalyzeFunctionPointer, SyntaxKind.FunctionPointerType);
        context.RegisterOperationAction(AnalyzeAnonymousFunction, OperationKind.AnonymousFunction);
    }

    private static void AnalyzeAnonymousFunction(OperationAnalysisContext context)
    {
        IAnonymousFunctionOperation function = (IAnonymousFunctionOperation)context.Operation;

        if (!ContainsTuple(function.Symbol.ReturnType))
            return;

        Location location = function.Syntax switch
        {
            LambdaExpressionSyntax lambda => lambda.ArrowToken.GetLocation(),
            AnonymousMethodExpressionSyntax method => method.DelegateKeyword.GetLocation(),
            _ => function.Syntax.GetLocation(),
        };

        context.ReportDiagnostic(Diagnostic.Create(Rule, location));
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        ISymbol? symbol = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken);
        ISymbol resultOwner;
        ITypeSymbol resultType;

        switch (symbol)
        {
            case IMethodSymbol method:
                {
                    resultOwner = method;
                    resultType = method.ReturnType;

                    break;
                }
            case IPropertySymbol property:
                {
                    resultOwner = property;
                    resultType = property.Type;

                    break;
                }
            case INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: not null } type:
                {
                    resultOwner = type;
                    resultType = type.DelegateInvokeMethod.ReturnType;

                    break;
                }
            default:
                return;
        }

        if (!ContainsTuple(resultType))
            return;

        if (HasPrescribedResult(resultOwner))
            return;

        TypeSyntax resultSyntax = context.Node switch
        {
            MethodDeclarationSyntax method => method.ReturnType,
            LocalFunctionStatementSyntax function => function.ReturnType,
            PropertyDeclarationSyntax property => property.Type,
            IndexerDeclarationSyntax indexer => indexer.Type,
            DelegateDeclarationSyntax declaration => declaration.ReturnType,
            OperatorDeclarationSyntax operation => operation.ReturnType,
            ConversionOperatorDeclarationSyntax conversion => conversion.Type,
            _ => throw new InvalidOperationException(message: "The result declaration has no return type."),
        };

        context.ReportDiagnostic(Diagnostic.Create(Rule, resultSyntax.GetLocation()));
    }

    private static void AnalyzeFunctionPointer(SyntaxNodeAnalysisContext context)
    {
        ITypeSymbol? type = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type;

        if (type is not IFunctionPointerTypeSymbol pointer)
            return;

        if (!ContainsTuple(pointer.Signature.ReturnType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }

    private static bool ContainsTuple(ITypeSymbol type)
    {
        return type switch
        {
            IArrayTypeSymbol array => ContainsTuple(array.ElementType),
            IPointerTypeSymbol pointer => ContainsTuple(pointer.PointedAtType),
            IFunctionPointerTypeSymbol => false,
            INamedTypeSymbol named => IsTupleFamily(named) || named.TypeArguments.Any(ContainsTuple),
            _ => false,
        };
    }

    private static bool HasPrescribedResult(ISymbol symbol)
    {
        bool prescribed = symbol switch
        {
            IMethodSymbol { OverriddenMethod: not null } => true,
            IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 } => true,
            IPropertySymbol { OverriddenProperty: not null } => true,
            IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 } => true,
            _ => false,
        };

        if (prescribed || symbol.ContainingType is not { TypeKind: not TypeKind.Interface } containingType)
            return prescribed;

        foreach (INamedTypeSymbol interfaceType in containingType.AllInterfaces)
        {
            foreach (ISymbol interfaceMember in interfaceType.GetMembers())
            {
                ISymbol? implementation = containingType.FindImplementationForInterfaceMember(interfaceMember);

                if (SymbolEqualityComparer.Default.Equals(implementation, symbol))
                    return true;
            }
        }

        return false;
    }

    private static bool IsTupleFamily(INamedTypeSymbol type)
    {
        INamedTypeSymbol definition = type.OriginalDefinition;

        return (type.IsTupleType || definition.Name is "Tuple" or "ValueTuple")
            && string.Equals(definition.ContainingNamespace.ToDisplayString(), b: "System", StringComparison.Ordinal);
    }
}
