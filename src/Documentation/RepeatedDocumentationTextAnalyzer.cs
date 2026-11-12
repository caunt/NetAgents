using System;
using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Documentation;

/// <summary>
/// Requires the tags documenting one public or protected member to say different things, so that no
/// parameter, result, or summary is described with words copied from another.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RepeatedDocumentationTextAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0035";

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Documentation tags of one member say different things",
        messageFormat: "Say what this <{0}> element adds; the <{1}> element above already carries the same text. Each tag answers "
            + "its own question about the member. " + DocumentationTags.HandWritten,
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

        // Case and a closing full stop do not make two descriptions different.
        Dictionary<string, string> written = new(StringComparer.OrdinalIgnoreCase);

        foreach (XmlNodeSyntax node in member.Elements)
        {
            if (node is not XmlElementSyntax element || !DocumentationTags.IsDescription(element))
                continue;

            string text = DocumentationTags.Flatten(element.Content.ToString()).TrimEnd(trimChars: ['.']);
            string name = DocumentationTags.GetName(element) ?? string.Empty;

            if (text.Length == 0)
                continue;

            if (written.TryGetValue(text, out string? first))
                context.ReportDiagnostic(Diagnostic.Create(Rule, element.GetLocation(), name, first));
            else
                written.Add(text, name);
        }
    }
}
