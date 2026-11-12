using System;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Documentation;

/// <summary>
/// Rejects documentation tags that do not fit the public or protected member they describe: a
/// <c>returns</c> where nothing is returned, a <c>value</c> outside a property or indexer, parameters
/// listed out of declaration order, an <c>inheritdoc</c> with nothing to inherit, and leftover
/// <c>placeholder</c> elements.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocumentationTagFitAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = DocumentationTags.TagFitRuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Documentation tags fit the member they describe",
        messageFormat: "{0}",
        category: "Documentation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration, DocumentationTags.DeclarationKinds);
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        DocumentedMember? member = DocumentationTags.Find(context.Node, context.SemanticModel, context.CancellationToken);

        if (member is null)
            return;

        bool returnsValue = DocumentationTags.ReturnsValue(member.Symbol);
        bool hasValue = member.Declaration is PropertyDeclarationSyntax or IndexerDeclarationSyntax;

        foreach (XmlNodeSyntax element in member.Elements)
        {
            switch (DocumentationTags.GetName(element))
            {
                case "returns" when !returnsValue:
                    {
                        Report(context, element, DocumentationTags.Remove, message: "Remove this <returns> element; the member returns no value");

                        break;
                    }
                case "value" when !hasValue:
                    {
                        Report(context, element, DocumentationTags.Remove, message: "Remove this <value> element; only a property or an indexer has a value");

                        break;
                    }
                case "inheritdoc" when DocumentationTags.GetCref(element) is null && !Inherits(member.Symbol):
                    {
                        Report(context,
                            element,
                            fix: null,
                            "Write this member's documentation; it overrides and implements nothing to inherit from. "
                            + DocumentationTags.HandWritten);

                        break;
                    }
                default:
                    break;
            }

            foreach (XmlNodeSyntax placeholder in element.DescendantNodesAndSelf().OfType<XmlNodeSyntax>())
            {
                if (string.Equals(DocumentationTags.GetName(placeholder), b: "placeholder", StringComparison.Ordinal))
                    Report(context, placeholder, DocumentationTags.Remove, message: "Remove this <placeholder> element and write the text it stands for");
            }
        }

        ImmutableArray<string> parameters = DocumentationTags.GetParameterNames(member.Declaration);
        ImmutableArray<string> typeParameters = DocumentationTags.GetTypeParameterNames(member.Declaration);

        ReportOrder(context, DocumentationTags.FindMisordered(member.Elements, element: "param", parameters), element: "param");
        ReportOrder(context, DocumentationTags.FindMisordered(member.Elements, element: "typeparam", typeParameters), element: "typeparam");
    }

    // Something is inherited when the member overrides or implements one, and a type or a constructor
    // inherits from its base type or the interfaces it implements.
    private static bool Inherits(ISymbol symbol)
    {
        switch (symbol)
        {
            case INamedTypeSymbol type:
                return type.Interfaces.Length > 0 || (type.BaseType is INamedTypeSymbol baseType && !IsRoot(baseType));
            case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                return constructor.ContainingType.BaseType is INamedTypeSymbol parent && !IsRoot(parent);
            case IMethodSymbol method when method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0:
            case IPropertySymbol property when property.IsOverride || property.ExplicitInterfaceImplementations.Length > 0:
            case IEventSymbol member when member.IsOverride || member.ExplicitInterfaceImplementations.Length > 0:
                return true;
            default:
                break;
        }

        if (symbol.ContainingType is not INamedTypeSymbol container)
            return false;

        foreach (INamedTypeSymbol contract in container.AllInterfaces)
        {
            foreach (ISymbol candidate in contract.GetMembers(symbol.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(container.FindImplementationForInterfaceMember(candidate), symbol))
                    return true;
            }
        }

        return false;
    }

    // Object, ValueType, and Enum stand above every type and carry no documentation of their own.
    private static bool IsRoot(INamedTypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType or SpecialType.System_Enum;
    }

    private static void Report(SyntaxNodeAnalysisContext context, XmlNodeSyntax element, string? fix, string message)
    {
        ImmutableDictionary<string, string?> properties = fix is null
            ? []
            : ImmutableDictionary<string, string?>.Empty.Add(DocumentationTags.FixProperty, fix);

        context.ReportDiagnostic(Diagnostic.Create(Rule, element.GetLocation(), properties, message));
    }

    private static void ReportOrder(SyntaxNodeAnalysisContext context, XmlNodeSyntax? misordered, string element)
    {
        if (misordered is null)
            return;

        Report(context, misordered, DocumentationTags.Order, $"List the <{element}> elements in the order the declaration lists them");
    }
}
