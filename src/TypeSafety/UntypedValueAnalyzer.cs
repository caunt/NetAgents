using System;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.Analyzers.TypeSafety;

/// <summary>
/// Rejects object, dynamic, and inferred untyped values.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UntypedValueAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0002";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Untyped values are forbidden",
        messageFormat: "Replace object or dynamic with an explicit type, interface, or generic type parameter",
        category: "TypeSafety",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeTypeName,
            SyntaxKind.PredefinedType, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
        context.RegisterOperationAction(AnalyzeValue,
            OperationKind.Invocation, OperationKind.PropertyReference, OperationKind.FieldReference,
            OperationKind.Conversion, OperationKind.Await, OperationKind.ArrayElementReference);
    }

    private static void AnalyzeTypeName(SyntaxNodeAnalysisContext context)
    {
        // Bind symbols so names such as 'value.ToString' are not mistaken for type declarations.
        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        ITypeSymbol? type = symbol as ITypeSymbol;
        if (context.Node is PredefinedTypeSyntax predefinedType && predefinedType.Keyword.IsKind(SyntaxKind.ObjectKeyword))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
        }
        else if (ContainsUntypedValue(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
        }
    }

    private static void AnalyzeValue(OperationAnalysisContext context)
    {
        if (!context.Operation.IsImplicit && ContainsUntypedValue(context.Operation.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation()));
        }
    }

    private static bool ContainsUntypedValue(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic)
        {
            return true;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            return ContainsUntypedValue(arrayType.ElementType);
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
            {
                if (ContainsUntypedValue(typeArgument))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
