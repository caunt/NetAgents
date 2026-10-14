using System;
using System.Collections.Immutable;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Readability;

internal static class QualifiedTypeImport
{
    public const string RuleIdentifier = "NETAGENTS0027";

    // A qualified type reference is fixable only when the name it would be shortened to binds to
    // this type and nothing else, and importing its namespace makes no other name in the file
    // ambiguous. Anything else still reports, flagged as needing an alias the author must choose.
    public static ImportResolution? Resolve(QualifiedNameSyntax name, SemanticModel model, CancellationToken cancellationToken)
    {
        if (!IsShortenable(name))
            return null;

        // An attribute name resolves to the constructor it selects rather than to the type itself.
        ISymbol? symbol = model.GetSymbolInfo(name, cancellationToken).Symbol;
        INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? (symbol as IMethodSymbol)?.ContainingType;

        if (type is null)
            return null;

        if (model.GetSymbolInfo(name.Left, cancellationToken).Symbol is not INamespaceSymbol container || container.IsGlobalNamespace)
            return null;

        // Generic types share a name across arities, and the arity written here picks between them,
        // so only a candidate of the same arity can stand in the way of the short name.
        ImmutableArray<ISymbol> bound = model.LookupNamespacesAndTypes(name.SpanStart, container: null, type.Name);
        bool resolves = false;

        foreach (ISymbol candidate in bound)
        {
            if (candidate is not INamedTypeSymbol named || named.Arity != type.Arity)
                continue;

            if (!IsSameType(named, type))
                return new(container, type.Name, import: true, collides: true);

            resolves = true;
        }

        // An attribute may be written without its suffix, and that shorter spelling has to be free too.
        string written = name.Right.Identifier.ValueText;

        if (!string.Equals(written, type.Name, StringComparison.Ordinal) && !IsWrittenNameFree(written, type, name, model))
            return new(container, type.Name, import: true, collides: true);

        if (resolves)
            return new(container, type.Name, import: false, collides: false);

        if (!ImportShadows(container, name, model, cancellationToken))
            return new(container, type.Name, import: true, collides: false);

        // The name is spoken for, so only an alias can shorten this reference. Choosing one renames a
        // type for the whole file, which is the author's decision, so this reports without a fix.
        return new(container, type.Name, import: true, collides: true);
    }

    private static bool ImportShadows(INamespaceSymbol container, QualifiedNameSyntax name, SemanticModel model, CancellationToken cancellationToken)
    {
        SyntaxNode root = name.SyntaxTree.GetRoot(cancellationToken);

        foreach (SyntaxNode node in root.DescendantNodes())
        {
            if (node is not IdentifierNameSyntax identifier)
                continue;

            // Only a name the namespace also declares can become a second candidate, and asking it
            // costs a lookup where binding the identifier costs the compiler a resolution.
            if (container.GetTypeMembers(identifier.Identifier.ValueText).IsEmpty)
                continue;

            // The import would offer a second candidate for a name this file already resolves
            // elsewhere. A declaration of another arity is never a second candidate for this one.
            if (model.GetSymbolInfo(identifier, cancellationToken).Symbol is not INamedTypeSymbol bound || IsDeclaredIn(container, bound))
                continue;

            if (!container.GetTypeMembers(identifier.Identifier.ValueText, bound.Arity).IsEmpty)
                return true;
        }

        return false;
    }

    private static bool IsDeclaredIn(INamespaceSymbol container, INamedTypeSymbol type)
    {
        INamespaceSymbol? declaring = type.OriginalDefinition.ContainingNamespace;

        return declaring is not null && string.Equals(declaring.ToDisplayString(), container.ToDisplayString(), StringComparison.Ordinal);
    }

    private static bool IsSameType(ISymbol bound, INamedTypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(bound.OriginalDefinition, type.OriginalDefinition);
    }

    // Only the outermost reference is a whole type name, and a name inside a using directive, a
    // namespace declaration, or nameof either declares the import or must stay spelled out.
    private static bool IsShortenable(QualifiedNameSyntax name)
    {
        if (name.Parent is QualifiedNameSyntax || name.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null)
            return false;

        BaseNamespaceDeclarationSyntax? declaration = name.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();

        if (declaration is not null && declaration.Name.Span.Contains(name.Span))
            return false;

        for (SyntaxNode? ancestor = name.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } })
                return false;
        }

        return true;
    }

    // Namespace symbols from different assemblies are distinct even when they name the same
    // namespace, so the comparison is by name rather than by symbol identity.
    private static bool IsWrittenNameFree(string written, INamedTypeSymbol type, QualifiedNameSyntax name, SemanticModel model)
    {
        foreach (ISymbol candidate in model.LookupNamespacesAndTypes(name.SpanStart, container: null, written))
        {
            if (candidate is INamedTypeSymbol named && !IsSameType(named, type))
                return false;
        }

        return true;
    }

    internal sealed class ImportResolution(INamespaceSymbol container, string name, bool import, bool collides)
    {
        public bool Collides { get; } = collides;

        public INamespaceSymbol Container { get; } = container;

        public bool Import { get; } = import;

        public string Name { get; } = name;
    }
}
