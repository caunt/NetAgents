using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Ordering;

internal static class MemberOrdering
{
    public const string RuleIdentifier = "NETAGENTS0022";

    public static bool IsOrdered(TypeDeclarationSyntax declaration)
    {
        return declaration.Members.ToFullString() == Sort(declaration).ToFullString();
    }

    public static SyntaxList<MemberDeclarationSyntax> Sort(TypeDeclarationSyntax declaration)
    {
        // Directives can control member availability, nullable state, and warning state.
        if (declaration.ContainsDirectives)
            return declaration.Members;

        MemberDeclarationSyntax[] members = [.. declaration.Members];

        MemberDeclarationSyntax[] sorted = [.. members
            .OrderBy(GetKind)
            .ThenBy(GetVisibility)
            .ThenBy(GetStorage)
            .ThenBy(static member => member.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) ? 0 : 1)
            .ThenBy(GetName, StringComparer.Ordinal)];

        // Preserve initializer execution order, including dependencies between fields and properties.
        Queue<MemberDeclarationSyntax> initializers = new(members.Where(HasInitializer));

        for (int index = 0; index < sorted.Length; index++)
        {
            if (HasInitializer(sorted[index]))
                sorted[index] = initializers.Dequeue();
        }

        // Field order is observable in value types and types with an explicit layout contract.
        bool preserveFieldOrder = declaration is StructDeclarationSyntax or RecordDeclarationSyntax { ClassOrStructKeyword.RawKind: (int)SyntaxKind.StructKeyword }
            || HasExplicitLayout(declaration);

        if (preserveFieldOrder)
        {
            // Keep all storage members in their original relative order, including initializers.
            Queue<MemberDeclarationSyntax> storage = new(members.Where(IsStorage));

            for (int index = 0; index < sorted.Length; index++)
            {
                if (IsStorage(sorted[index]))
                    sorted[index] = storage.Dequeue();
            }
        }

        // Blank line separators belong to the slot in the file, not to the member that used to occupy it.
        for (int index = 0; index < sorted.Length; index++)
            sorted[index] = WithSlotSeparator(sorted[index], members[index]);

        return SyntaxFactory.List(sorted);
    }

    private static int CountSeparator(SyntaxTriviaList trivia)
    {
        int count = 0;

        while (count < trivia.Count && (trivia[count].IsKind(SyntaxKind.WhitespaceTrivia) || trivia[count].IsKind(SyntaxKind.EndOfLineTrivia)))
            count++;

        return count;
    }

    private static int GetKind(MemberDeclarationSyntax member)
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
            _ => 13
        };
    }

    private static string GetName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            BaseFieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? string.Empty,
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => destructor.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            EventDeclarationSyntax eventDeclaration => eventDeclaration.Identifier.ValueText,
            DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.Identifier.ValueText,
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.OperatorToken.ValueText,
            ConversionOperatorDeclarationSyntax conversion => conversion.Type.ToString(),
            _ => string.Empty
        };
    }

    private static int GetStorage(MemberDeclarationSyntax member)
    {
        return member.Modifiers.Any(SyntaxKind.ConstKeyword) ? 0 : member.Modifiers.Any(SyntaxKind.StaticKeyword) ? 1 : 2;
    }

    private static int GetVisibility(MemberDeclarationSyntax member)
    {
        SyntaxTokenList modifiers = member.Modifiers;

        return modifiers.Any(SyntaxKind.PublicKeyword)
            ? 0
            : modifiers.Any(SyntaxKind.ProtectedKeyword)
            ? modifiers.Any(SyntaxKind.InternalKeyword) ? 2 : modifiers.Any(SyntaxKind.PrivateKeyword) ? 4 : 3
            : modifiers.Any(SyntaxKind.InternalKeyword)
            ? 1
            : modifiers.Any(SyntaxKind.PrivateKeyword) ? 5 : member.Parent is InterfaceDeclarationSyntax ? 0 : 5;
    }

    private static bool HasExplicitLayout(TypeDeclarationSyntax declaration)
    {
        foreach (AttributeListSyntax list in declaration.AttributeLists)
        {
            foreach (AttributeSyntax attribute in list.Attributes)
            {
                if (attribute.Name.ToString().Contains(value: "StructLayout"))
                    return true;
            }
        }

        return false;
    }

    private static bool HasInitializer(MemberDeclarationSyntax member)
    {
        if (member is BaseFieldDeclarationSyntax field && !field.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
            {
                if (variable.Initializer is not null)
                    return true;
            }
        }

        return member is PropertyDeclarationSyntax { Initializer: not null };
    }

    private static bool IsStorage(MemberDeclarationSyntax member)
    {
        return member is BaseFieldDeclarationSyntax or PropertyDeclarationSyntax;
    }

    private static MemberDeclarationSyntax WithSlotSeparator(MemberDeclarationSyntax member, MemberDeclarationSyntax slot)
    {
        SyntaxTriviaList trivia = member.GetLeadingTrivia();
        SyntaxTriviaList separator = slot.GetLeadingTrivia();
        int adopted = CountSeparator(separator);
        int discarded = CountSeparator(trivia);
        List<SyntaxTrivia> replacement = [];

        for (int index = 0; index < adopted; index++)
            replacement.Add(separator[index]);

        for (int index = discarded; index < trivia.Count; index++)
            replacement.Add(trivia[index]);

        return member.WithLeadingTrivia(replacement);
    }
}
