using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Documentation;

/// <summary>
/// Requires the documentation of public and protected members to write <see langword="null"/>, <see langword="true"/>, and
/// <see langword="false"/> as <c>langword</c> references, and a Boolean result or value to read as the .NET
/// documentation words one: <see langword="true"/> if something holds; otherwise, <see langword="false"/>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LanguageKeywordAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = DocumentationTags.KeywordRuleIdentifier;

    // A hyphenated word such as "non-null" is prose about the keyword rather than the keyword itself.
    private static readonly Regex KeywordPattern = new(pattern: @"(?<![\w-])(null|true|false)(?![\w-])", RegexOptions.CultureInvariant);

    // The same words outside an attribute: a keyword already quoted inside a reference is not wrapped again.
    private static readonly Regex PlainBooleanPattern = new(pattern: @"(?<![\w""-])(true|false)(?![\w""-])", RegexOptions.CultureInvariant);
    private static readonly Regex LanguageWordPattern = new(pattern: @"<see\s+langword\s*=\s*""(\w+)""\s*/>|<c>\s*(null|true|false)\s*</c>", RegexOptions.CultureInvariant);
    private static readonly Regex BooleanPattern = new(pattern: @"^<see langword=""true""/> if .+; otherwise, <see langword=""false""/>\.?$", RegexOptions.CultureInvariant);

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Documentation writes language keywords as langword references",
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

        if (member is null)
            return;

        foreach (XmlNodeSyntax node in member.Elements)
        {
            if (node is not XmlElementSyntax element || !DocumentationTags.IsDescription(element))
                continue;

            ReportKeywords(context, element);

            if (IsBooleanDescription(member, element) && !ReadsAsBoolean(element))
            {
                string message = $"Word this <{DocumentationTags.GetName(element)}> element as '<see langword=\"true\"/> if …; "
                    + "otherwise, <see langword=\"false\"/>.', naming the exact condition that makes it true. " + DocumentationTags.HandWritten;

                context.ReportDiagnostic(Diagnostic.Create(Rule, element.GetLocation(), message));
            }
        }
    }

    // A Boolean method's <returns> and a Boolean property's or indexer's <value>.
    private static bool IsBooleanDescription(DocumentedMember member, XmlElementSyntax element)
    {
        return DocumentationTags.GetName(element) switch
        {
            "returns" => member.Symbol is IMethodSymbol { ReturnType.SpecialType: SpecialType.System_Boolean },
            "value" => member.Symbol is IPropertySymbol { Type.SpecialType: SpecialType.System_Boolean },
            _ => false,
        };
    }

    // The wording is judged apart from the markup: a plain true or false is read as the reference it
    // should be, which the keyword report already asks for and its fix writes.
    private static bool ReadsAsBoolean(XmlElementSyntax element)
    {
        string text = DocumentationTags.Flatten(element.Content.ToString());

        text = LanguageWordPattern.Replace(text,
            static match => "<see langword=\"" + (match.Groups[groupnum: 1].Success ? match.Groups[groupnum: 1].Value : match.Groups[groupnum: 2].Value) + "\"/>");

        text = PlainBooleanPattern.Replace(text, static match => "<see langword=\"" + match.Value + "\"/>");

        return BooleanPattern.IsMatch(text);
    }

    private static void Report(SyntaxNodeAnalysisContext context, TextSpan span, string keyword)
    {
        ImmutableDictionary<string, string?> properties = ImmutableDictionary<string, string?>.Empty.Add(DocumentationTags.KeywordProperty, keyword);
        string message = $"Write '{keyword}' as <see langword=\"{keyword}\"/>";

        context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Node.SyntaxTree, span), properties, message));
    }

    // Text inside code markup is code, except a <c> holding nothing but the keyword, which is the
    // keyword written the old way.
    private static void ReportKeywords(SyntaxNodeAnalysisContext context, XmlElementSyntax element)
    {
        foreach (XmlNodeSyntax node in element.Content)
        {
            switch (node)
            {
                case XmlTextSyntax text:
                    {
                        foreach (SyntaxToken token in text.TextTokens)
                        {
                            if (!token.IsKind(SyntaxKind.XmlTextLiteralToken))
                                continue;

                            foreach (Match match in KeywordPattern.Matches(token.Text).Cast<Match>())
                                Report(context, new TextSpan(token.SpanStart + match.Index, match.Length), match.Value);
                        }

                        break;
                    }
                case XmlElementSyntax nested when DocumentationTags.GetName(nested) is "c":
                    {
                        string code = nested.Content.ToString().Trim();

                        if (code is "null" or "true" or "false")
                            Report(context, nested.Span, code);

                        break;
                    }
                case XmlElementSyntax nested when DocumentationTags.GetName(nested) is "code" or "see" or "seealso":
                    break;
                case XmlElementSyntax nested:
                    {
                        ReportKeywords(context, nested);

                        break;
                    }
                default:
                    break;
            }
        }
    }
}
