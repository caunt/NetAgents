using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Documentation;

// What the documentation rules share: the comment above a declaration, its elements, and the facts
// about the declaration a tag has to agree with.
internal static class DocumentationTags
{
    // Keys of the facts a diagnostic carries to its fix, so the fix need not work out again what the rule found:
    // the cref and kind a constructor's opening names, how a tag is repaired, and the keyword a langword replaces.
    public const string CrefProperty = "Cref";
    public const string FixProperty = "Fix";
    public const string KeywordProperty = "Keyword";
    public const string KindProperty = "Kind";

    // The repairs the fix property names: putting the tags in the declaration's order, or removing one.
    public const string Order = "Order";
    public const string Remove = "Remove";

    // Every report asking for words ends with what makes them worth reading. The rules can tell that a
    // tag is missing or empty; only the author can make it useful, so the message says what useful means.
    public const string HandWritten = "Write it by hand for the people using this API: explain what actually happens, which a name "
        + "cannot say, and add a <remarks> element on the behavior behind the member or an <example> with a short code sample "
        + "wherever its use is not obvious.";

    // The rules whose fixes live in the code-fix assembly, which sees this file but not the analyzers.
    public const string KeywordRuleIdentifier = "NETAGENTS0038";
    public const string SentenceRuleIdentifier = "NETAGENTS0036";
    public const string SummaryOpeningRuleIdentifier = "NETAGENTS0037";
    public const string TagFitRuleIdentifier = "NETAGENTS0034";

    // The declarations a documentation comment can describe, which are the ones CS1591 asks about.
    public static readonly ImmutableArray<SyntaxKind> DeclarationKinds =
    [
        SyntaxKind.ClassDeclaration,
        SyntaxKind.StructDeclaration,
        SyntaxKind.RecordDeclaration,
        SyntaxKind.RecordStructDeclaration,
        SyntaxKind.InterfaceDeclaration,
        SyntaxKind.EnumDeclaration,
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.EnumMemberDeclaration,
        SyntaxKind.MethodDeclaration,
        SyntaxKind.ConstructorDeclaration,
        SyntaxKind.OperatorDeclaration,
        SyntaxKind.ConversionOperatorDeclaration,
        SyntaxKind.PropertyDeclaration,
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.EventDeclaration,
        SyntaxKind.EventFieldDeclaration,
        SyntaxKind.FieldDeclaration,
    ];

    // The elements whose text describes the declaration, as opposed to the markup used inside that text.
    public static readonly ImmutableHashSet<string> Descriptions = ImmutableHashSet.Create(StringComparer.Ordinal, items: ["summary", "param", "typeparam", "returns", "value", "exception", "remarks"]);

    private static readonly Regex ExteriorPattern = new(pattern: @"\r?\n[ \t]*///", RegexOptions.CultureInvariant);
    private static readonly Regex WhitespacePattern = new(pattern: @"\s+", RegexOptions.CultureInvariant);

    // The comment's top-level elements, or nothing when the declaration is not documented or the
    // project does not parse documentation comments.
    public static DocumentedMember? Find(SyntaxNode declaration, SemanticModel model, CancellationToken cancellationToken)
    {
        List<XmlNodeSyntax> elements = GetElements(declaration);

        if (elements.Count == 0)
            return null;

        ISymbol? symbol = declaration switch
        {
            BaseFieldDeclarationSyntax field when field.Declaration.Variables.Count > 0
                => model.GetDeclaredSymbol(field.Declaration.Variables[index: 0], cancellationToken),
            BaseFieldDeclarationSyntax => null,
            _ => model.GetDeclaredSymbol(declaration, cancellationToken),
        };

        return symbol is null || !IsExternallyVisible(symbol) ? null : new DocumentedMember(declaration, symbol, elements);
    }

    // The declaration a comment describes: the first declaration the comment's token belongs to.
    public static SyntaxNode? FindDeclaration(DocumentationCommentTriviaSyntax comment)
    {
        for (SyntaxNode? node = comment.ParentTrivia.Token.Parent; node is not null; node = node.Parent)
        {
            if (DeclarationKinds.Contains(node.Kind()))
                return node;
        }

        return null;
    }

    // The first <param> or <typeparam> written out of the order the declaration lists its parameters,
    // or nothing when they are in order. A list naming something the declaration lacks, or naming one
    // twice, is the compiler's to report, and is left as written.
    public static XmlNodeSyntax? FindMisordered(List<XmlNodeSyntax> elements, string element, ImmutableArray<string> declared)
    {
        List<XmlNodeSyntax> written = GetNamed(elements, element);
        List<int> positions = [];

        foreach (XmlNodeSyntax node in written)
        {
            int position = declared.IndexOf(GetAttribute(node, attribute: "name") ?? string.Empty);

            if (position < 0 || positions.Contains(position))
                return null;

            positions.Add(position);
        }

        for (int index = 1; index < positions.Count; index++)
        {
            if (positions[index] < positions[index - 1])
                return written[index];
        }

        return null;
    }

    // Where a full stop belongs when an element's text does not end a sentence: after its last word or
    // its last inline element. Nothing is returned for text that already ends with a full stop, a
    // question, or an exclamation, for an empty element, and for text closing on a list, a code block,
    // or a table, which are not sentences; a closing paragraph is judged by its own last sentence.
    public static int? FindUnpunctuatedEnd(XmlElementSyntax element)
    {
        for (int index = element.Content.Count - 1; index >= 0; index--)
        {
            XmlNodeSyntax node = element.Content[index];

            switch (node)
            {
                case XmlTextSyntax text:
                    {
                        for (int position = text.TextTokens.Count - 1; position >= 0; position--)
                        {
                            SyntaxToken token = text.TextTokens[position];

                            if (token.IsKind(SyntaxKind.XmlTextLiteralNewLineToken))
                                continue;

                            string written = token.Text.TrimEnd();

                            if (written.Length == 0)
                                continue;

                            bool ends = token.IsKind(SyntaxKind.XmlTextLiteralToken) && written[written.Length - 1] is '.' or '!' or '?';

                            return ends ? null : token.SpanStart + written.Length;
                        }

                        continue;
                    }
                case XmlElementSyntax nested when GetName(nested) is "para":
                    return FindUnpunctuatedEnd(nested);
                case XmlElementSyntax nested when GetName(nested) is "list" or "code" or "table":
                    return null;
                case XmlCommentSyntax:
                    continue;
                default:
                    return node.Span.End;
            }
        }

        return null;
    }

    // Layout is not meaning: the exterior markers and line breaks of a wrapped comment are removed
    // and every run of whitespace becomes one space.
    public static string Flatten(string text)
    {
        return WhitespacePattern.Replace(ExteriorPattern.Replace(text, replacement: " "), replacement: " ").Trim();
    }

    public static string? GetAttribute(XmlNodeSyntax node, string attribute)
    {
        foreach (XmlAttributeSyntax candidate in GetAttributes(node))
        {
            if (!string.Equals(candidate.Name.LocalName.ValueText, attribute, StringComparison.Ordinal))
                continue;

            return candidate switch
            {
                XmlNameAttributeSyntax named => named.Identifier.Identifier.ValueText,
                XmlCrefAttributeSyntax cref => cref.Cref.ToString(),
                XmlTextAttributeSyntax text => text.TextTokens.ToString(),
                _ => null,
            };
        }

        return null;
    }

    public static SyntaxList<XmlAttributeSyntax> GetAttributes(XmlNodeSyntax node)
    {
        return node switch
        {
            XmlElementSyntax element => element.StartTag.Attributes,
            XmlEmptyElementSyntax element => element.Attributes,
            _ => default,
        };
    }

    public static CrefSyntax? GetCref(XmlNodeSyntax node)
    {
        foreach (XmlAttributeSyntax attribute in GetAttributes(node))
        {
            if (attribute is XmlCrefAttributeSyntax cref)
                return cref.Cref;
        }

        return null;
    }

    // Consecutive comment lines parse into one comment, but a blank line between two blocks leaves two,
    // and the compiler reads both, so their elements are gathered together.
    public static List<XmlNodeSyntax> GetElements(SyntaxNode declaration)
    {
        List<XmlNodeSyntax> elements = [];

        foreach (SyntaxTrivia trivia in declaration.GetLeadingTrivia())
        {
            if (trivia.GetStructure() is not DocumentationCommentTriviaSyntax comment)
                continue;

            foreach (XmlNodeSyntax node in comment.Content)
            {
                if (node is XmlElementSyntax or XmlEmptyElementSyntax)
                    elements.Add(node);
            }
        }

        return elements;
    }

    // Where a report about the declaration as a whole belongs: the name it introduces.
    public static Location GetIdentifierLocation(SyntaxNode declaration)
    {
        SyntaxToken identifier = declaration switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier,
            DelegateDeclarationSyntax type => type.Identifier,
            EnumMemberDeclarationSyntax member => member.Identifier,
            MethodDeclarationSyntax method => method.Identifier,
            ConstructorDeclarationSyntax constructor => constructor.Identifier,
            OperatorDeclarationSyntax operation => operation.OperatorToken,
            ConversionOperatorDeclarationSyntax conversion => conversion.OperatorKeyword,
            PropertyDeclarationSyntax property => property.Identifier,
            IndexerDeclarationSyntax indexer => indexer.ThisKeyword,
            EventDeclarationSyntax member => member.Identifier,
            BaseFieldDeclarationSyntax field when field.Declaration.Variables.Count > 0 => field.Declaration
                .Variables[index: 0]
                .Identifier,
            _ => default,
        };

        return identifier.IsKind(SyntaxKind.None) ? declaration.GetLocation() : identifier.GetLocation();
    }

    public static string? GetName(XmlNodeSyntax node)
    {
        XmlNameSyntax? name = node switch
        {
            XmlElementSyntax element => element.StartTag.Name,
            XmlEmptyElementSyntax element => element.Name,
            _ => null,
        };

        return name?.LocalName.ValueText;
    }

    public static List<XmlNodeSyntax> GetNamed(List<XmlNodeSyntax> elements, string element)
    {
        List<XmlNodeSyntax> named = [];

        foreach (XmlNodeSyntax node in elements)
        {
            if (string.Equals(GetName(node), element, StringComparison.Ordinal))
                named.Add(node);
        }

        return named;
    }

    // The names in declaration order, read with plain loops: the syntax lists are structs, and LINQ over
    // them would box each one.
    public static ImmutableArray<string> GetParameterNames(SyntaxNode declaration)
    {
        ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>();

        foreach (ParameterSyntax parameter in GetParameters(declaration))
            names.Add(parameter.Identifier.ValueText);

        return names.ToImmutable();
    }

    // The parameters a <param> element can name: a member's own, a delegate's, or the positional
    // parameters of a record or a primary constructor written on the type.
    public static SeparatedSyntaxList<ParameterSyntax> GetParameters(SyntaxNode declaration)
    {
        BaseParameterListSyntax? parameters = declaration switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList,
            DelegateDeclarationSyntax type => type.ParameterList,
            IndexerDeclarationSyntax indexer => indexer.ParameterList,
            TypeDeclarationSyntax type => type.ParameterList,
            _ => null,
        };

        return parameters?.Parameters ?? default;
    }

    public static ImmutableArray<string> GetTypeParameterNames(SyntaxNode declaration)
    {
        ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>();

        foreach (TypeParameterSyntax parameter in GetTypeParameters(declaration))
            names.Add(parameter.Identifier.ValueText);

        return names.ToImmutable();
    }

    public static SeparatedSyntaxList<TypeParameterSyntax> GetTypeParameters(SyntaxNode declaration)
    {
        TypeParameterListSyntax? parameters = declaration switch
        {
            TypeDeclarationSyntax type => type.TypeParameterList,
            DelegateDeclarationSyntax type => type.TypeParameterList,
            MethodDeclarationSyntax method => method.TypeParameterList,
            _ => null,
        };

        return parameters?.Parameters ?? default;
    }

    // The tag names a member only after its own words are written, and a comment that takes those from
    // somewhere else, inherited or included from a file, has nothing here to check.
    public static bool HasBorrowedText(List<XmlNodeSyntax> elements)
    {
        foreach (XmlNodeSyntax element in elements)
        {
            string? name = GetName(element);

            if (name is "inheritdoc" or "include")
                return true;
        }

        return false;
    }

    public static bool IsDescription(XmlNodeSyntax node)
    {
        return GetName(node) is string name && Descriptions.Contains(name);
    }

    // The members CS1591 asks about: everything outside the assembly can see, through every containing
    // type, which is public, protected, and protected internal.
    public static bool IsExternallyVisible(ISymbol symbol)
    {
        for (ISymbol? current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                return false;
        }

        return true;
    }

    // A member that hands back a value describes it with <returns>: methods, delegates, and operators,
    // except those returning nothing or a Task or ValueTask that carries no result. Properties and
    // indexers describe theirs with <value>.
    public static bool ReturnsValue(ISymbol symbol)
    {
        IMethodSymbol? method = symbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } type => type.DelegateInvokeMethod,
            IMethodSymbol candidate => candidate,
            _ => null,
        };

        return method is not null && method.MethodKind is not MethodKind.Constructor and not MethodKind.StaticConstructor && !method.ReturnsVoid && !IsResultlessTask(method.ReturnType);
    }

    private static bool IsResultlessTask(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { IsGenericType: false } named && named.Name is "Task" or "ValueTask" && named.ContainingNamespace?.ToDisplayString() is "System.Threading.Tasks";
    }
}

