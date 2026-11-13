using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Documentation;

/// <summary>
/// Requires the documentation of a public or protected member to carry every tag its signature calls
/// for: a <c>param</c> for each parameter, a <c>typeparam</c> for each type parameter, <c>returns</c>
/// for a result, <c>value</c> for a property or indexer, and an <c>exception</c> for each exception
/// its own body throws.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequiredDocumentationAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0033";

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Documentation carries every tag the signature calls for",
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

        // Inherited or included text is written somewhere this comment cannot see.
        if (member is null || DocumentationTags.HasBorrowedText(member.Elements))
            return;

        HashSet<string> parameters = Collect(member.Elements, element: "param");
        HashSet<string> typeParameters = Collect(member.Elements, element: "typeparam");

        foreach (ParameterSyntax parameter in DocumentationTags.GetParameters(member.Declaration))
        {
            string name = parameter.Identifier.ValueText;

            if (name.Length > 0 && name != "_" && !parameters.Contains(name))
            {
                Report(context,
                    parameter.Identifier.GetLocation(),
                    $"Describe the parameter '{name}' with a <param> element: what the "
                    + "argument means to this member, which values it accepts and in what units or format, and what happens with an "
                    + "empty, missing, or out-of-range one.");
            }
        }

        foreach (TypeParameterSyntax parameter in DocumentationTags.GetTypeParameters(member.Declaration))
        {
            string name = parameter.Identifier.ValueText;

            if (!typeParameters.Contains(name))
            {
                Report(context,
                    parameter.Identifier.GetLocation(),
                    $"Describe the type parameter '{name}' with a <typeparam> element: the "
                    + "role the type plays, what it has to support, and what a caller typically supplies for it.");
            }
        }

        Location identifier = DocumentationTags.GetIdentifierLocation(member.Declaration);

        if (DocumentationTags.ReturnsValue(member.Symbol) && !Contains(member.Elements, element: "returns"))
        {
            Report(context,
                identifier,
                "Describe the result with a <returns> element: what the value is, how it follows from "
                + "the arguments, and what comes back when there is nothing to return, the input is empty, or the work is cancelled.");
        }

        bool hasValue = member.Declaration is PropertyDeclarationSyntax or IndexerDeclarationSyntax;

        if (hasValue && !Contains(member.Elements, element: "value"))
        {
            Report(context,
                identifier,
                "Describe the value with a <value> element: what it holds, its default, its units or "
                + "range, and what changes it, rather than the summary's words again.");
        }

        ReportUndocumentedExceptions(context, member);
    }

    private static HashSet<string> Collect(List<XmlNodeSyntax> elements, string element)
    {
        List<string> names = [];

        foreach (XmlNodeSyntax node in elements)
        {
            string? name = DocumentationTags.GetAttribute(node, attribute: "name");

            if (name is not null && string.Equals(DocumentationTags.GetName(node), element, StringComparison.Ordinal))
                names.Add(name);
        }

        return new HashSet<string>(names, StringComparer.Ordinal);
    }

    private static bool Contains(List<XmlNodeSyntax> elements, string element)
    {
        return elements.Any(node => string.Equals(DocumentationTags.GetName(node), element, StringComparison.Ordinal));
    }

    // A documented exception covers the ones derived from it, as a caller catching it catches those too.
    private static bool Covers(List<INamedTypeSymbol> documented, INamedTypeSymbol thrown)
    {
        for (INamedTypeSymbol? current = thrown; current is not null; current = current.BaseType)
        {
            if (documented.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, current.OriginalDefinition)))
                return true;
        }

        return false;
    }

    // The bodies that run when the member is used: a method's or operator's, and each accessor's.
    // A type's members document themselves, and a field initializer is not the member's behavior.
    private static IEnumerable<SyntaxNode> GetBodies(SyntaxNode declaration)
    {
        switch (declaration)
        {
            case BaseMethodDeclarationSyntax method:
                {
                    if (method.Body is not null)
                        yield return method.Body;

                    if (method.ExpressionBody is not null)
                        yield return method.ExpressionBody;

                    if (method is ConstructorDeclarationSyntax { Initializer: not null } constructor)
                        yield return constructor.Initializer;

                    break;
                }
            case BasePropertyDeclarationSyntax property:
                {
                    if (property is PropertyDeclarationSyntax { ExpressionBody: not null } expressed)
                        yield return expressed.ExpressionBody;

                    if (property is IndexerDeclarationSyntax { ExpressionBody: not null } indexed)
                        yield return indexed.ExpressionBody;

                    foreach (AccessorDeclarationSyntax accessor in property.AccessorList?.Accessors ?? default)
                        yield return accessor;

                    break;
                }
            default:
                break;
        }
    }

    // Exception types declare static ThrowIf helpers that throw their own type, or a type derived from it.
    private static INamedTypeSymbol? GetThrowHelperException(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        string? name = (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText;

        return name is null || !name.StartsWith(value: "ThrowIf", StringComparison.Ordinal)
            ? null
            : model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { IsStatic: true, ContainingType: INamedTypeSymbol type }
            ? null
            : IsException(type) ? type : null;
    }

    // A throw inside a try block whose catch clauses take that exception never leaves the member.
    private static bool IsCaught(SyntaxNode throwing, INamedTypeSymbol thrown, SyntaxNode declaration, SemanticModel model)
    {
        for (SyntaxNode? node = throwing; node is not null && node != declaration; node = node.Parent)
        {
            if (node.Parent is not TryStatementSyntax attempt || attempt.Block != node)
                continue;

            foreach (CatchClauseSyntax clause in attempt.Catches)
            {
                if (clause.Declaration is null || model.GetTypeInfo(clause.Declaration.Type).Type is not INamedTypeSymbol caught)
                    return true;

                if (Covers(documented: [caught], thrown))
                    return true;
            }
        }

        return false;
    }

    private static bool IsException(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            string? container = current.ContainingNamespace?.ToDisplayString();

            if (current.Name == "Exception" && container is "System")
                return true;
        }

        return false;
    }

    private static void Report(SyntaxNodeAnalysisContext context, Location location, string message)
    {
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, message + " " + DocumentationTags.HandWritten));
    }

    // Exceptions the member's own body throws: throw statements and expressions, and the throw helpers
    // exception types declare, such as ArgumentNullException.ThrowIfNull. What a callee throws, and what
    // a lambda or local function throws whenever it happens to run, is out of reach.
    private static void ReportUndocumentedExceptions(SyntaxNodeAnalysisContext context, DocumentedMember member)
    {
        List<INamedTypeSymbol> documented = [];

        foreach (XmlNodeSyntax element in member.Elements)
        {
            CrefSyntax? cref = DocumentationTags.GetCref(element);

            if (cref is null || DocumentationTags.GetName(element) is not "exception")
                continue;

            if (context.SemanticModel.GetSymbolInfo(cref, context.CancellationToken).Symbol is INamedTypeSymbol type)
                documented.Add(type);
        }

        HashSet<INamedTypeSymbol> reported = new(SymbolEqualityComparer.Default);

        foreach (SyntaxNode body in GetBodies(member.Declaration))
        {
            IEnumerable<SyntaxNode> nodes = body.DescendantNodesAndSelf(static node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));

            foreach (SyntaxNode node in nodes)
            {
                INamedTypeSymbol? thrown = node switch
                {
                    ThrowStatementSyntax { Expression: ExpressionSyntax expression } => context.SemanticModel
                        .GetTypeInfo(expression, context.CancellationToken)
                        .Type as INamedTypeSymbol,
                    ThrowExpressionSyntax expression => context.SemanticModel
                        .GetTypeInfo(expression.Expression, context.CancellationToken)
                        .Type as INamedTypeSymbol,
                    InvocationExpressionSyntax invocation => GetThrowHelperException(invocation, context.SemanticModel),
                    _ => null,
                };

                if (thrown is null || thrown.TypeKind == TypeKind.Error || Covers(documented, thrown))
                    continue;

                if (IsCaught(node, thrown, member.Declaration, context.SemanticModel))
                    continue;

                if (reported.Add(thrown.OriginalDefinition))
                {
                    Report(context,
                        node.GetLocation(),
                        $"Describe when '{thrown.Name}' is thrown with an <exception> element: the exact "
                        + "condition, naming the argument or state that causes it, so a caller knows how to avoid or handle it.");
                }
            }
        }
    }
}
