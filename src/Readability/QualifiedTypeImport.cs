using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Readability;

internal static class QualifiedTypeImport
{
    public const string RuleIdentifier = "NETAGENTS0027";

    // The simple names of every namespace the compilation can see, nested ones included. A member
    // access starts with a namespace only when its leftmost name is one of these, which lets the
    // analyzer skip binding the far more common accesses on locals, members, and types.
    public static ImmutableHashSet<string> CollectNamespaceNames(Compilation compilation)
    {
        List<string> names = [];
        Stack<INamespaceSymbol> pending = new();

        pending.Push(compilation.GlobalNamespace);

        // Namespaces of the same name sit in different places, such as System.Text and
        // Microsoft.CodeAnalysis.Text, so every one is walked even when its name is already known.
        while (pending.Count > 0)
        {
            foreach (INamespaceSymbol nested in pending.Pop().GetNamespaceMembers())
            {
                names.Add(nested.Name);
                pending.Push(nested);
            }
        }

        return ImmutableHashSet.CreateRange(StringComparer.Ordinal, names);
    }

    // The leftmost name of a member access chain made only of simple names, which is where a namespace
    // qualifier would start; anything else, such as an invocation or an alias-qualified name, has none.
    public static IdentifierNameSyntax? GetLeftmostName(MemberAccessExpressionSyntax access)
    {
        ExpressionSyntax current = access.Expression;

        while (current is MemberAccessExpressionSyntax inner)
        {
            // A namespace is spelled with plain names only, so a generic name or a pointer access ends it.
            if (!inner.IsKind(SyntaxKind.SimpleMemberAccessExpression) || inner.Name is not IdentifierNameSyntax)
                return null;

            current = inner.Expression;
        }

        return current as IdentifierNameSyntax;
    }

    // The name a qualified reference shortens to: the right-hand side of a qualified type name, or the
    // member name of an access such as System.Math in System.Math.Min(...), type arguments included.
    public static SimpleNameSyntax? GetShortName(SyntaxNode reference)
    {
        return reference switch
        {
            QualifiedNameSyntax name => name.Right,
            MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) => access.Name,
            _ => null,
        };
    }

    // A qualified type reference is fixable only when the name it would be shortened to binds to
    // this type and nothing else, and importing its namespace makes no other name in the file
    // ambiguous. Anything else still reports, flagged as needing an alias the author must choose.
    public static ImportResolution? Resolve(ExpressionSyntax reference, SemanticModel model, CancellationToken cancellationToken)
    {
        ExpressionSyntax? qualifier = reference switch
        {
            QualifiedNameSyntax name => name.Left,
            MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) => access.Expression,
            _ => null,
        };

        if (qualifier is null || GetShortName(reference) is not SimpleNameSyntax shortName || !IsShortenable(reference))
            return null;

        // An attribute name resolves to the constructor it selects rather than to the type itself.
        ISymbol? symbol = model.GetSymbolInfo(reference, cancellationToken).Symbol;
        INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? (symbol as IMethodSymbol)?.ContainingType;

        if (type is null)
            return null;

        if (model.GetSymbolInfo(qualifier, cancellationToken).Symbol is not INamespaceSymbol container || container.IsGlobalNamespace)
            return null;

        if (reference is MemberAccessExpressionSyntax && IsTakenByValue(type, reference, model))
            return new(container, type.Name, import: true, collides: true);

        ImmutableArray<ISymbol> bound = model.LookupNamespacesAndTypes(reference.SpanStart, container: null, type.Name);
        bool resolves = false;

        foreach (ISymbol candidate in bound)
        {
            if (candidate is not INamedTypeSymbol named)
                continue;

            // Generic types share a name across arities, and the arity written here picks between them,
            // so only a candidate of the same arity can stand in the way of the short name.
            if (named.Arity != type.Arity)
                continue;

            if (!IsSameType(named, type))
                return new(container, type.Name, import: true, collides: true);

            resolves = true;
        }

        // An attribute may be written without its suffix, and that shorter spelling has to be free too.
        string written = shortName.Identifier.ValueText;

        if (!string.Equals(written, type.Name, StringComparison.Ordinal) && !IsWrittenNameFree(written, type, reference, model))
            return new(container, type.Name, import: true, collides: true);

        if (resolves)
            return new(container, type.Name, import: false, collides: false);

        if (!ImportShadows(container, reference, model, cancellationToken))
            return new(container, type.Name, import: true, collides: false);

        // The name is spoken for, so only an alias can shorten this reference. Choosing one renames a
        // type for the whole file, which is the author's decision, so this reports without a fix.
        return new(container, type.Name, import: true, collides: true);
    }

    private static bool ImportShadows(INamespaceSymbol container, ExpressionSyntax reference, SemanticModel model, CancellationToken cancellationToken)
    {
        SyntaxNode root = reference.SyntaxTree.GetRoot(cancellationToken);

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

    // A name inside a using directive, a namespace declaration, or nameof either declares the import
    // or must stay spelled out. Elsewhere only the part ending in the type is reported, wherever it
    // sits in a longer name: System.Environment in System.Environment.SpecialFolder, while the
    // namespace before it binds to no type and the nested type after it is qualified by a type.
    private static bool IsShortenable(ExpressionSyntax reference)
    {
        if (reference.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null)
            return false;

        BaseNamespaceDeclarationSyntax? declaration = reference.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();

        if (declaration is not null && declaration.Name.Span.Contains(reference.Span))
            return false;

        for (SyntaxNode? ancestor = reference.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } })
                return false;
        }

        return true;
    }

    // In an expression the short name is looked up among locals, parameters, and members before types,
    // so one of those spelled the same takes it, as does a namespace. A value of the type's own name and
    // type does not: the language still reads Status.Active as the type when a Status property named
    // Status is in scope.
    private static bool IsTakenByValue(INamedTypeSymbol type, ExpressionSyntax reference, SemanticModel model)
    {
        foreach (ISymbol candidate in model.LookupSymbols(reference.SpanStart, container: null, type.Name))
        {
            if (candidate is INamedTypeSymbol)
                continue;

            ITypeSymbol? valueType = candidate switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                _ => null,
            };

            if (valueType is null || !IsSameType(valueType, type))
                return true;
        }

        return false;
    }

    // Namespace symbols from different assemblies are distinct even when they name the same
    // namespace, so the comparison is by name rather than by symbol identity.
    private static bool IsWrittenNameFree(string written, INamedTypeSymbol type, ExpressionSyntax reference, SemanticModel model)
    {
        foreach (ISymbol candidate in model.LookupNamespacesAndTypes(reference.SpanStart, container: null, written))
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
