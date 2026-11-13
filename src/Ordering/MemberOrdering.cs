using System;
using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Ordering;

/// <summary>
/// Puts the members of a type in order and places the blank lines between them.
/// </summary>
/// <remarks>
/// <para>
/// Members sort by bucket, which is kind, visibility, storage, and mutability (see <see cref="MemberKeys"/>), then by
/// name, except where their order is observable (see <see cref="MemberInitializers"/>). Blank lines then follow the
/// groups of <see cref="MemberSpacing"/>, so a member never keeps the blank line that happened to sit where it lands.
/// </para>
/// <para>
/// Within a bucket, a field, property, or event that carries a comment line heads a section: the members written
/// after it stay below it, sorted by name, so a comment can name the purpose of the members gathered under it.
/// </para>
/// </remarks>
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

        List<MemberDeclarationSyntax> sorted = Arrange(declaration);
        List<MemberDeclarationSyntax> spaced = [];

        for (int index = 0; index < sorted.Count; index++)
        {
            MemberDeclarationSyntax member = index == 0 ? MemberSpacing.WithOpeningSpace(sorted[index], declaration.Members[index: 0]) : sorted[index];

            spaced.Add(MemberSpacing.WithSpacing(member, index == 0 ? null : sorted[index - 1]));
        }

        return SyntaxFactory.List(spaced);
    }

    // Each step takes the member that sorts first among those free to move, and a member of a chain is free only once
    // every member written before it in that chain has been placed. Without chains this is a stable sort.
    private static List<MemberDeclarationSyntax> Arrange(TypeDeclarationSyntax declaration)
    {
        List<MemberDeclarationSyntax> remaining = [.. declaration.Members];
        List<MemberDeclarationSyntax> arranged = [];
        Sections sections = new(declaration);

        // The chains of members that keep their written order, and how many of each chain are placed so far.
        List<List<MemberDeclarationSyntax>> chains = MemberInitializers.GetChains(declaration);
        int[] placed = new int[chains.Count];

        while (remaining.Count > 0)
        {
            int chosen = -1;

            for (int index = 0; index < remaining.Count; index++)
            {
                bool free = IsFree(remaining[index], chains, placed);

                if (free && (chosen < 0 || sections.Compare(remaining[index], remaining[chosen]) < 0))
                    chosen = index;
            }

            MemberDeclarationSyntax next = remaining[chosen];

            for (int chain = 0; chain < chains.Count; chain++)
            {
                if (placed[chain] < chains[chain].Count && chains[chain][placed[chain]] == next)
                    placed[chain]++;
            }

            arranged.Add(next);
            remaining.RemoveAt(chosen);
        }

        return arranged;
    }

    private static bool IsFree(MemberDeclarationSyntax member, List<List<MemberDeclarationSyntax>> chains, int[] placed)
    {
        for (int chain = 0; chain < chains.Count; chain++)
        {
            int position = chains[chain].IndexOf(member);

            if (position > placed[chain])
                return false;
        }

        return true;
    }

    // The sections of a type, numbered per bucket in written order, and the members that head them.
    private sealed class Sections
    {
        private readonly HashSet<MemberDeclarationSyntax> _headers = [];
        private readonly Dictionary<MemberDeclarationSyntax, int> _numbers = [];

        public Sections(TypeDeclarationSyntax declaration)
        {
            Dictionary<string, int> current = new(StringComparer.Ordinal);

            foreach (MemberDeclarationSyntax member in declaration.Members)
            {
                if (member is not BaseFieldDeclarationSyntax and not PropertyDeclarationSyntax)
                    continue;

                string bucket = MemberKeys.GetBucketKey(member);
                int number = current.TryGetValue(bucket, out int last) ? last : 0;

                if (MemberSpacing.StartsGroup(member))
                {
                    number++;

                    _headers.UnionWith(other: [member]);
                }

                current[bucket] = number;
                _numbers[member] = number;
            }
        }

        public int Compare(MemberDeclarationSyntax left, MemberDeclarationSyntax right)
        {
            int bucket = MemberKeys.CompareBuckets(left, right);
            int section = GetNumber(left).CompareTo(GetNumber(right));

            if (bucket != 0 || section != 0)
                return bucket != 0 ? bucket : section;

            // The member carrying the comment stays first, so the comment still heads its section.
            int header = _headers.Contains(right).CompareTo(_headers.Contains(left));

            return header != 0 ? header : string.CompareOrdinal(MemberKeys.GetName(left), MemberKeys.GetName(right));
        }

        private int GetNumber(MemberDeclarationSyntax member)
        {
            return _numbers.TryGetValue(member, out int number) ? number : 0;
        }
    }
}
