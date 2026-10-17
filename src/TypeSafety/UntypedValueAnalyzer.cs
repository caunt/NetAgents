using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.TypeSafety;

/// <summary>
/// Rejects object, dynamic, and inferred untyped values.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UntypedValueAnalyzer() : PolicyAnalyzer(Rule, analyzesRazorCode: true)
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
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterCompilationStartAction(
            start =>
        {
            ImmutableHashSet<SyntaxTree> aliased = GetAliasedTrees(start.Compilation, start.CancellationToken);

            start.RegisterSyntaxNodeAction(node => AnalyzeTypeName(node, aliased), SyntaxKind.PredefinedType, SyntaxKind.IdentifierName, SyntaxKind.GenericName);

            start.RegisterOperationAction(
                AnalyzeValue,
                OperationKind.Invocation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference,
                OperationKind.Conversion,
                OperationKind.Await,
                OperationKind.ArrayElementReference
            );
        }
        );
    }

    private static void AnalyzeTypeName(SyntaxNodeAnalysisContext context, ImmutableHashSet<SyntaxTree> aliased)
    {
        if (context.Node is PredefinedTypeSyntax predefinedType)
        {
            // Of the predefined types only object is untyped, which the keyword already says.
            if (predefinedType.Keyword.IsKind(SyntaxKind.ObjectKeyword) && !HasPrescribedSignature(context))
                context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));

            return;
        }

        // An untyped type is written as one of three names, and each spelling inside a generic
        // argument or array is a node visited in its own right, so binding every identifier in the
        // project to find them is wasted. Only an alias can hide one, and only in a file that has one.
        if (!IsUntypedSpelling(context.Node) && !aliased.Contains(context.Node.SyntaxTree))
            return;

        // Bind symbols so names such as 'value.ToString' are not mistaken for type declarations.
        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;

        if (ContainsUntypedValue(symbol as ITypeSymbol) && !HasPrescribedSignature(context))
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }

    private static void AnalyzeValue(OperationAnalysisContext context)
    {
        if (!context.Operation.IsImplicit && ContainsUntypedValue(context.Operation.Type))
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation()));
    }

    private static bool ContainsUntypedValue(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic)
            return true;

        if (type is IArrayTypeSymbol arrayType)
            return ContainsUntypedValue(arrayType.ElementType);

        if (type is INamedTypeSymbol namedType)
        {
            foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
            {
                if (ContainsUntypedValue(typeArgument))
                    return true;
            }
        }

        return false;
    }

    // A using alias is the one way an untyped type reaches a name that does not say so. Aliases are
    // declared per file, except a global one, which puts every file back on the binding path.
    private static ImmutableHashSet<SyntaxTree> GetAliasedTrees(Compilation compilation, CancellationToken cancellationToken)
    {
        // A list rather than a set: the same tree may be added twice and the set below dedupes.
        List<SyntaxTree> aliased = [];
        bool global = false;

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (tree.GetRoot(cancellationToken) is not CompilationUnitSyntax unit)
                continue;

            foreach (UsingDirectiveSyntax directive in unit.Usings)
            {
                if (directive.Alias is null)
                    continue;

                global |= !directive.GlobalKeyword.IsKind(SyntaxKind.None);
                aliased.Add(tree);
            }

            foreach (SyntaxNode node in unit.DescendantNodes(static candidate => candidate is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax))
            {
                if (node is not BaseNamespaceDeclarationSyntax declaration)
                    continue;

                foreach (UsingDirectiveSyntax directive in declaration.Usings)
                {
                    if (directive.Alias is not null)
                        aliased.Add(tree);
                }
            }
        }

        return global ? [.. compilation.SyntaxTrees] : [.. aliased];
    }

    private static bool HasPrescribedSignature(SyntaxNodeAnalysisContext context)
    {
        SyntaxNode? declaration = context.Node;

        while (declaration is TypeSyntax)
            declaration = declaration.Parent;

        // Overrides and explicit implementations inherit their parameter and result types; bodies stay strict.
        MemberDeclarationSyntax? member = declaration switch
        {
            ParameterSyntax parameter => parameter.Parent?.Parent as MemberDeclarationSyntax,
            MemberDeclarationSyntax owner => owner,
            _ => null,
        };

        return member is not null
            && context.SemanticModel.GetDeclaredSymbol(member, context.CancellationToken)
                is { IsOverride: true }
                or IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 }
                or IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 };
    }

    private static bool IsUntypedSpelling(SyntaxNode node)
    {
        return node is IdentifierNameSyntax identifier && identifier.Identifier.ValueText is "dynamic" or "var" or "Object";
    }
}
