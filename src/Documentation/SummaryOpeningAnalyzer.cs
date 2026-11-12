using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Documentation;

/// <summary>
/// Requires the summaries of public and protected properties and constructors to open as the .NET API
/// documentation does: a property with "Gets", "Sets", or "Gets or sets" as its visible accessors
/// allow, and a constructor with "Initializes a new instance of the" type it creates.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SummaryOpeningAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = DocumentationTags.SummaryOpeningRuleIdentifier;

    // Longest first, so "Gets or sets" is never read as a plain "Gets".
    private static readonly ImmutableArray<string> PropertyOpenings = ["Gets or sets", "Gets or initializes", "Gets", "Sets"];

    private static readonly Regex ConstructorPattern = new(pattern: "^Initializes a new instance of the <see cref=\"[^\"]+\"\\s*/> (class|struct|record)(?![\\w-])",
        RegexOptions.CultureInvariant);

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Property and constructor summaries open as the .NET documentation does",
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

    // The constructor's own type, named through a cref, and what kind of type it is.
    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context, IMethodSymbol constructor, XmlElementSyntax summary, string text)
    {
        INamedTypeSymbol type = constructor.ContainingType;
        string kind = type.TypeKind == TypeKind.Struct ? "struct" : type.IsRecord ? "record" : "class";
        Match opening = ConstructorPattern.Match(text);

        if (opening.Success && opening.Groups[groupnum: 1].Value == kind && NamesType(summary, type, context.SemanticModel))
            return;

        string cref = type.TypeParameters.Length == 0 ? type.Name : type.Name + "{" + string.Join(separator: ",", type.TypeParameters.Select(static parameter => parameter.Name)) + "}";

        ImmutableDictionary<string, string?> properties = ImmutableDictionary<string, string?>.Empty
            .Add(DocumentationTags.CrefProperty, cref)
            .Add(DocumentationTags.KindProperty, kind);

        string message = $"Open the summary with 'Initializes a new instance of the <see cref=\"{cref}\"/> {kind}'";

        context.ReportDiagnostic(Diagnostic.Create(Rule, summary.GetLocation(), properties, message));
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        DocumentedMember? member = DocumentationTags.Find(context.Node, context.SemanticModel, context.CancellationToken);
        XmlElementSyntax? summary = member?.Elements.OfType<XmlElementSyntax>().FirstOrDefault(static element => DocumentationTags.GetName(element) is "summary");

        if (member is null || summary is null)
            return;

        string text = DocumentationTags.Flatten(summary.Content.ToString());

        switch (member.Symbol)
        {
            case IPropertySymbol { IsIndexer: false } property:
                {
                    AnalyzeProperty(context, property, summary, text);

                    break;
                }
            case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                {
                    AnalyzeConstructor(context, constructor, summary, text);

                    break;
                }
            default:
                break;
        }
    }

    // Only the accessors a caller outside the assembly can use count, so a private setter is no "sets".
    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context, IPropertySymbol property, XmlElementSyntax summary, string text)
    {
        bool gets = property.GetMethod is IMethodSymbol getter && DocumentationTags.IsExternallyVisible(getter);
        bool sets = property.SetMethod is IMethodSymbol setter && DocumentationTags.IsExternallyVisible(setter);
        bool initializes = sets && property.SetMethod?.IsInitOnly == true;

        string expected = gets && sets ? "Gets or sets" : gets ? "Gets" : "Sets";
        string written = GetPropertyOpening(text);

        if (written == expected || (initializes && written == "Gets or initializes"))
            return;

        string message = $"Open the summary with '{expected}', which is what the accessors of '{property.Name}' let a caller do, "
            + "and go on to say what the property means to them. " + DocumentationTags.HandWritten;

        context.ReportDiagnostic(Diagnostic.Create(Rule, summary.GetLocation(), message));
    }

    private static string GetPropertyOpening(string text)
    {
        foreach (string opening in PropertyOpenings)
        {
            if (text.StartsWith(opening + " ", StringComparison.Ordinal))
                return opening;
        }

        return string.Empty;
    }

    // The opening's cref has to name this very type, so a summary copied from another type is caught.
    private static bool NamesType(XmlElementSyntax summary, INamedTypeSymbol type, SemanticModel model)
    {
        CrefSyntax? cref = null;

        foreach (XmlNodeSyntax node in summary.Content)
        {
            if (DocumentationTags.GetName(node) is "see")
            {
                cref = DocumentationTags.GetCref(node);

                break;
            }
        }

        return cref is not null && model.GetSymbolInfo(cref).Symbol is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, type.OriginalDefinition);
    }
}
