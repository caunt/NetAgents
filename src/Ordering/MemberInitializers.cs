using System;
using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Ordering;

/// <summary>
/// Finds the members whose relative order is observable, so sorting keeps them in the order they were written.
/// </summary>
/// <remarks>
/// Field and property initializers run in the order they are written, static ones when the type is first used and
/// instance ones when an object is created. Moving a member without an initializer changes nothing, and neither does
/// moving one whose initializer only reads constants, literals, or constructor parameters. What must stay in order
/// are initializers that run code, as in <c>Id = Counter.Next();</c>, and every initializer of a phase where one
/// reads another member, as in <c>Total = Count + 1;</c>. In a struct, a type with an explicit layout, or a type
/// that serializers read, every field and property keeps its place, since their order is the layout or the output.
/// </remarks>
internal static class MemberInitializers
{
    // Attribute names, without the Attribute suffix, that mark a type whose fields and properties serializers,
    // object-relational mappers, or interop read in declaration order.
    private static readonly HashSet<string> SerializedTypeMarkers = new(StringComparer.Ordinal) { "DataContract", "MessagePackObject", "ProtoContract", "Serializable", "StructLayout" };

    // Attribute names that mark a single field or property a serializer reads, which marks its type the same way.
    private static readonly HashSet<string> SerializedMemberMarkers = new(StringComparer.Ordinal) { "Column", "DataMember", "ProtoMember" };

    // Each list is one chain: its members keep their written order relative to one another.
    public static List<List<MemberDeclarationSyntax>> GetChains(TypeDeclarationSyntax declaration)
    {
        List<List<MemberDeclarationSyntax>> chains = [];

        if (KeepsStorageOrder(declaration))
        {
            List<MemberDeclarationSyntax> storage = [];

            foreach (MemberDeclarationSyntax member in declaration.Members)
            {
                if (member is BaseFieldDeclarationSyntax or PropertyDeclarationSyntax)
                    storage.Add(member);
            }

            chains.Add(storage);

            return chains;
        }

        HashSet<string> names = CollectMemberNames(declaration);

        chains.Add(GetPhaseChain(declaration, names, isStatic: true));
        chains.Add(GetPhaseChain(declaration, names, isStatic: false));

        return chains;
    }

    private static HashSet<string> CollectMemberNames(TypeDeclarationSyntax declaration)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (MemberDeclarationSyntax member in declaration.Members)
        {
            // A constant is folded in at compile time, so reading one depends on no initializer having run.
            if (member is BaseFieldDeclarationSyntax field && !field.Modifiers.Any(SyntaxKind.ConstKeyword))
            {
                foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                    names.UnionWith(other: [variable.Identifier.ValueText]);
            }
            else if (member is not BaseFieldDeclarationSyntax and not BaseTypeDeclarationSyntax)
            {
                names.UnionWith(other: [MemberKeys.GetName(member)]);
            }
        }

        return names;
    }

    private static string GetAttributeName(AttributeSyntax attribute)
    {
        string name = attribute.Name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => attribute.Name.ToString(),
        };

        return name.EndsWith(value: "Attribute", StringComparison.Ordinal) ? name.Substring(startIndex: 0, name.Length - "Attribute".Length) : name;
    }

    private static List<ExpressionSyntax> GetInitializers(MemberDeclarationSyntax member)
    {
        List<ExpressionSyntax> values = [];

        if (member is BaseFieldDeclarationSyntax field && !field.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
            {
                if (variable.Initializer is not null)
                    values.Add(variable.Initializer.Value);
            }
        }

        if (member is PropertyDeclarationSyntax { Initializer: not null } property)
            values.Add(property.Initializer.Value);

        return values;
    }

    private static List<MemberDeclarationSyntax> GetPhaseChain(TypeDeclarationSyntax declaration, HashSet<string> names, bool isStatic)
    {
        List<MemberDeclarationSyntax> initialized = [];
        List<MemberDeclarationSyntax> effectful = [];
        bool reads = false;

        foreach (MemberDeclarationSyntax member in declaration.Members)
        {
            List<ExpressionSyntax> values = GetInitializers(member);

            if (values.Count == 0 || member.Modifiers.Any(SyntaxKind.StaticKeyword) != isStatic)
                continue;

            initialized.Add(member);

            foreach (ExpressionSyntax value in values)
            {
                reads |= ReadsMember(value, names);

                if (RunsCode(value) && !effectful.Contains(member))
                    effectful.Add(member);
            }
        }

        // Once one initializer reads another member, any of them may be the one it reads.
        return reads ? initialized : effectful;
    }

    // Json and Xml attributes come in many names, such as JsonPropertyName, JsonInclude, XmlElement, and XmlIgnore,
    // and every one of them belongs to a serializer.
    private static bool HasMarker(SyntaxList<AttributeListSyntax> lists, HashSet<string> markers)
    {
        foreach (AttributeListSyntax list in lists)
        {
            foreach (AttributeSyntax attribute in list.Attributes)
            {
                string name = GetAttributeName(attribute);
                bool serializer = name.StartsWith(value: "Json", StringComparison.Ordinal) || name.StartsWith(value: "Xml", StringComparison.Ordinal);

                if (serializer || markers.Contains(name))
                    return true;
            }
        }

        return false;
    }

    private static bool KeepsStorageOrder(TypeDeclarationSyntax declaration)
    {
        bool valueType = declaration is StructDeclarationSyntax || declaration.Keyword.IsKind(SyntaxKind.StructKeyword);

        bool recordStruct = declaration is RecordDeclarationSyntax record && record.ClassOrStructKeyword
            .IsKind(SyntaxKind.StructKeyword);

        if (valueType || recordStruct)
            return true;

        if (HasMarker(declaration.AttributeLists, SerializedTypeMarkers))
            return true;

        foreach (MemberDeclarationSyntax member in declaration.Members)
        {
            if (member is BaseFieldDeclarationSyntax or PropertyDeclarationSyntax && HasMarker(member.AttributeLists, SerializedMemberMarkers))
                return true;
        }

        return false;
    }

    private static bool ReadsMember(ExpressionSyntax value, HashSet<string> names)
    {
        foreach (SyntaxNode node in value.DescendantNodesAndSelf(static node => node is not AnonymousFunctionExpressionSyntax))
        {
            // The name after a dot belongs to whatever stands before it, unless that is this object.
            bool qualified = node.Parent is MemberAccessExpressionSyntax access && access.Name == node && access.Expression is not ThisExpressionSyntax;

            if (node is IdentifierNameSyntax identifier && !qualified && names.Contains(identifier.Identifier.ValueText))
                return true;
        }

        return false;
    }

    // A lambda's body runs later, when it is called, so only code outside lambdas runs during initialization.
    private static bool RunsCode(ExpressionSyntax value)
    {
        foreach (SyntaxNode node in value.DescendantNodesAndSelf(static node => node is not AnonymousFunctionExpressionSyntax))
        {
            bool call = node is InvocationExpressionSyntax { Expression: not IdentifierNameSyntax { Identifier.ValueText: "nameof" } };

            if (call || node is BaseObjectCreationExpressionSyntax or AwaitExpressionSyntax)
                return true;
        }

        return false;
    }
}
