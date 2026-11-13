using System;
using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Ordering;

/// <summary>
/// The parts of a member that decide where it sorts: kind, visibility, storage, mutability, and name, in that order.
/// Members that share every part but the name form one bucket, and each bucket is a group of its own when blank
/// lines are placed.
/// </summary>
internal static class MemberKeys
{
    // Buckets sort by kind, visibility, storage, and mutability, and constants then by type, so a size never sits
    // between two messages: int before string, and string before uint.
    public static int CompareBuckets(MemberDeclarationSyntax left, MemberDeclarationSyntax right)
    {
        int bucket = GetBucket(left).CompareTo(GetBucket(right));

        if (bucket != 0)
            return bucket;

        string leftType = GetConstantType(left);
        string rightType = GetConstantType(right);
        int ignoringCase = string.Compare(leftType, rightType, StringComparison.OrdinalIgnoreCase);

        return ignoringCase != 0 ? ignoringCase : string.CompareOrdinal(leftType, rightType);
    }

    // The kind, visibility, storage, and mutability of a member as one number that sorts the way the four do in turn;
    // each part stays below ten except the kind, which leads.
    public static int GetBucket(MemberDeclarationSyntax member)
    {
        return (GetKind(member) * 1000) + (GetVisibility(member) * 100) + (GetStorage(member) * 10) + GetMutability(member);
    }

    // One bucket as text, for telling buckets apart: the number from GetBucket and, for a constant, its type.
    public static string GetBucketKey(MemberDeclarationSyntax member)
    {
        return GetBucket(member).ToString(CultureInfo.InvariantCulture) + "|" + GetConstantType(member);
    }

    // The type a constant is declared with, as written; every other member has none.
    public static string GetConstantType(MemberDeclarationSyntax member)
    {
        return member is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword) ? field.Declaration
            .Type
            .ToString() : string.Empty;
    }

    public static int GetKind(MemberDeclarationSyntax member)
    {
        return member switch
        {
            FieldDeclarationSyntax => 0,
            ConstructorDeclarationSyntax => 1,
            DestructorDeclarationSyntax => 2,
            EventFieldDeclarationSyntax or EventDeclarationSyntax => 3,
            EnumDeclarationSyntax => 4,
            InterfaceDeclarationSyntax => 5,
            PropertyDeclarationSyntax => 6,
            IndexerDeclarationSyntax => 7,
            OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => 8,
            MethodDeclarationSyntax => 9,
            StructDeclarationSyntax => 10,
            RecordDeclarationSyntax record when record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => 10,
            ClassDeclarationSyntax or RecordDeclarationSyntax => 11,
            DelegateDeclarationSyntax => 12,
            _ => 13,
        };
    }

    // Members fixed once construction ends come first: readonly fields, and properties nobody outside a constructor
    // or object initializer can set, which is get-only, init, required, and computed properties.
    public static int GetMutability(MemberDeclarationSyntax member)
    {
        return member switch
        {
            BaseFieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) || field.Modifiers.Any(SyntaxKind.ConstKeyword) ? 0 : 1,
            PropertyDeclarationSyntax property => IsSettable(property) ? 1 : 0,
            _ => member.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) ? 0 : 1,
        };
    }

    public static string GetName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            BaseFieldDeclarationSyntax field => field.Declaration.Variables.Count > 0 ? field.Declaration
                .Variables[index: 0]
                .Identifier
                .ValueText : string.Empty,
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => destructor.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            EventDeclarationSyntax eventDeclaration => eventDeclaration.Identifier.ValueText,
            DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.Identifier.ValueText,
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.OperatorToken.ValueText,
            ConversionOperatorDeclarationSyntax conversion => conversion.Type.ToString(),
            _ => string.Empty,
        };
    }

    public static int GetStorage(MemberDeclarationSyntax member)
    {
        return member.Modifiers.Any(SyntaxKind.ConstKeyword) ? 0 : member.Modifiers.Any(SyntaxKind.StaticKeyword) ? 1 : 2;
    }

    public static int GetVisibility(MemberDeclarationSyntax member)
    {
        SyntaxTokenList modifiers = member.Modifiers;

        return modifiers.Any(SyntaxKind.PublicKeyword)
            ? 0
            : modifiers.Any(SyntaxKind.ProtectedKeyword)
            ? modifiers.Any(SyntaxKind.InternalKeyword) ? 2 : modifiers.Any(SyntaxKind.PrivateKeyword) ? 4 : 3
            : modifiers.Any(SyntaxKind.InternalKeyword)
            ? 1
            : modifiers.Any(SyntaxKind.PrivateKeyword) || member.Parent is not InterfaceDeclarationSyntax ? 5 : 0;
    }

    private static bool IsSettable(PropertyDeclarationSyntax property)
    {
        if (property.Modifiers.Any(SyntaxKind.RequiredKeyword) || property.AccessorList is null)
            return false;

        foreach (AccessorDeclarationSyntax accessor in property.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration))
                return true;
        }

        return false;
    }
}
