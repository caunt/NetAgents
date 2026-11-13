using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Ordering;

/// <summary>
/// Places blank lines between the members of a type the way statement groups place them between statements.
/// </summary>
/// <remarks>
/// Fields, properties, and field-like events written on one line, without documentation or attributes, group with
/// their neighbours of the same bucket: same kind, visibility, storage, and mutability. One blank line separates
/// groups and none sits inside one. A comment line opens a new group. Every other member stands alone: methods,
/// constructors, nested types, and any member spanning lines or carrying documentation or attributes.
/// </remarks>
internal static class MemberSpacing
{
    public const int Limit = 4;

    public static List<MemberGroup> GetLargeGroups(TypeDeclarationSyntax declaration)
    {
        List<MemberGroup> groups = [];
        SyntaxList<MemberDeclarationSyntax> members = declaration.Members;
        int start = 0;

        // The pass runs one step past the last member, so the final group closes like any other.
        for (int index = 1; index <= members.Count; index++)
        {
            if (index < members.Count && !IsSeparated(members[index - 1], members[index]))
                continue;

            if (index - start > Limit && !StoresParameters(declaration, members, start, index))
                groups.Add(new MemberGroup(members[start], members[index - 1], Describe(members[start]), index - start));

            start = index;
        }

        return groups;
    }

    public static bool IsSeparated(MemberDeclarationSyntax previous, MemberDeclarationSyntax current)
    {
        return StandsAlone(previous) || StandsAlone(current) || MemberKeys.CompareBuckets(previous, current) != 0 || StartsGroup(current);
    }

    public static bool StartsGroup(MemberDeclarationSyntax member)
    {
        foreach (SyntaxTrivia trivia in member.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                return true;
        }

        return false;
    }

    // The member that sorts first takes the blank lines the first member had, since the space after the opening
    // brace belongs to NETAGENTS0023 and not to whichever member used to stand there.
    public static MemberDeclarationSyntax WithOpeningSpace(MemberDeclarationSyntax member, MemberDeclarationSyntax original)
    {
        if (member == original || !StartsLine(member) || !StartsLine(original))
            return member;

        List<SyntaxTrivia> trivia = [];

        foreach (SyntaxTrivia piece in original.GetLeadingTrivia())
        {
            if (!piece.IsKind(SyntaxKind.EndOfLineTrivia) && !piece.IsKind(SyntaxKind.WhitespaceTrivia))
                break;

            if (piece.IsKind(SyntaxKind.EndOfLineTrivia))
                trivia.Add(piece);
        }

        trivia.AddRange(WithoutBlankLines(member.GetLeadingTrivia()));

        return member.WithLeadingTrivia(trivia);
    }

    // Blank lines go between members only. The space after the opening brace belongs to NETAGENTS0023, and a member
    // sharing a line with the one before it keeps its place.
    public static MemberDeclarationSyntax WithSpacing(MemberDeclarationSyntax member, MemberDeclarationSyntax? previous)
    {
        if (previous is null || !EndsLine(previous))
            return member;

        SyntaxTrivia lineBreak = previous.GetTrailingTrivia()[previous.GetTrailingTrivia().Count - 1];
        List<SyntaxTrivia> trivia = IsSeparated(previous, member) ? [lineBreak] : [];

        trivia.AddRange(WithoutBlankLines(member.GetLeadingTrivia()));

        return member.WithLeadingTrivia(trivia);
    }

    private static string Describe(MemberDeclarationSyntax member)
    {
        return member switch
        {
            PropertyDeclarationSyntax => "properties",
            EventFieldDeclarationSyntax => "events",
            _ => "fields",
        };
    }

    private static bool EndsLine(MemberDeclarationSyntax member)
    {
        SyntaxTriviaList trailing = member.GetTrailingTrivia();

        return trailing.Count > 0 && trailing[trailing.Count - 1].IsKind(SyntaxKind.EndOfLineTrivia);
    }

    private static bool HasDocumentation(MemberDeclarationSyntax member)
    {
        foreach (SyntaxTrivia trivia in member.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                return true;
        }

        return false;
    }

    // A primary constructor parameter as it is, or behind a null check: _first = first ?? throw ...;.
    private static bool IsParameter(ExpressionSyntax? value, ParameterListSyntax parameters)
    {
        ExpressionSyntax? stored = value is BinaryExpressionSyntax { Right: ThrowExpressionSyntax } check && check.IsKind(SyntaxKind.CoalesceExpression) ? check.Left : value;

        if (stored is not IdentifierNameSyntax name)
            return false;

        foreach (ParameterSyntax parameter in parameters.Parameters)
        {
            if (parameter.Identifier.ValueText == name.Identifier.ValueText)
                return true;
        }

        return false;
    }

    private static bool StandsAlone(MemberDeclarationSyntax member)
    {
        return member is not BaseFieldDeclarationSyntax and not PropertyDeclarationSyntax || member.AttributeLists.Count > 0 || HasDocumentation(member) || member.ToString().Contains(value: "\n");
    }

    // A member starts a line of its own when the token before it ends one, which its leading trivia cannot show.
    private static bool StartsLine(MemberDeclarationSyntax member)
    {
        SyntaxTriviaList trailing = member.GetFirstToken().GetPreviousToken().TrailingTrivia;

        return trailing.Count > 0 && trailing[trailing.Count - 1].IsKind(SyntaxKind.EndOfLineTrivia);
    }

    // Fields and properties that each store one primary constructor parameter repeat the signature one line at a time,
    // so a comment between them could only restate it, as for constructor statements that copy their parameters.
    private static bool StoresParameters(TypeDeclarationSyntax declaration, SyntaxList<MemberDeclarationSyntax> members, int start, int end)
    {
        if (declaration.ParameterList is not ParameterListSyntax parameters)
            return false;

        for (int index = start; index < end; index++)
        {
            ExpressionSyntax? value = members[index] switch
            {
                BaseFieldDeclarationSyntax field when field.Declaration.Variables.Count == 1 => field.Declaration.Variables[index: 0].Initializer?.Value,
                PropertyDeclarationSyntax property => property.Initializer?.Value,
                _ => null,
            };

            if (!IsParameter(value, parameters))
                return false;
        }

        return true;
    }

    // A line holding nothing but whitespace goes; comment lines, documentation, and the indentation before the
    // declaration stay as written.
    private static List<SyntaxTrivia> WithoutBlankLines(SyntaxTriviaList trivia)
    {
        List<SyntaxTrivia> kept = [];
        List<SyntaxTrivia> indentation = [];
        bool lineStart = true;

        foreach (SyntaxTrivia piece in trivia)
        {
            if (piece.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                indentation.Add(piece);

                continue;
            }

            bool blankLine = lineStart && piece.IsKind(SyntaxKind.EndOfLineTrivia);

            if (!blankLine)
            {
                kept.AddRange(indentation);
                kept.Add(piece);
            }

            indentation.Clear();

            lineStart = blankLine || piece.IsKind(SyntaxKind.EndOfLineTrivia) || piece.HasStructure;
        }

        kept.AddRange(indentation);

        return kept;
    }
}
