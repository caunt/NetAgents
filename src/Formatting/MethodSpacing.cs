using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Formatting;

internal static class MethodSpacing
{
    public const string RuleIdentifier = "NETAGENTS0021";

    public static ImmutableDictionary<TextSpan, TextChange> GetChanges(SyntaxNode root, SourceText source)
    {
        ImmutableDictionary<TextSpan, TextChange>.Builder changes = ImmutableDictionary.CreateBuilder<TextSpan, TextChange>();

        foreach (TypeDeclarationSyntax declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            for (int index = 1; index < declaration.Members.Count; index++)
            {
                MemberDeclarationSyntax previous = declaration.Members[index - 1];
                MemberDeclarationSyntax current = declaration.Members[index];

                if (previous is not BaseMethodDeclarationSyntax || current is not BaseMethodDeclarationSyntax)
                    continue;

                TextChange? change = DeclarationSpacing.GetChange(previous, current, source);

                if (change is TextChange spacingChange)
                    changes.Add(current.GetFirstToken().Span, spacingChange);
            }
        }

        return changes.ToImmutable();
    }
}
