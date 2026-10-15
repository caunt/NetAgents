using System;
using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Documentation;

/// <summary>
/// Requires overloads sharing a name to describe what distinguishes them from one another.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OverloadSummaryAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0030";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Overloads describe what separates them",
        messageFormat: "Say what this overload of '{0}' does differently; an earlier one already carries this summary",
        category: "Documentation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(
            AnalyzeType,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration,
            SyntaxKind.InterfaceDeclaration
        );
    }

    // Overloads are siblings, so the comparison belongs to the type that declares them. Members of
    // different types may share a summary freely; only a reader choosing between two of them loses.
    private static void AnalyzeType(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not TypeDeclarationSyntax declaration)
            return;

        Dictionary<string, string> described = new(StringComparer.Ordinal);

        foreach (MemberDeclarationSyntax member in declaration.Members)
        {
            if (member is not MethodDeclarationSyntax method)
                continue;

            string? summary = GetSummary(method);

            if (summary is null)
                continue;

            string name = method.Identifier.ValueText;

            if (!described.TryGetValue(name, out string? first))
            {
                described[name] = summary;

                continue;
            }

            if (string.Equals(first, summary, StringComparison.Ordinal))
                context.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation(), name));
        }
    }

    private static string Flatten(string text)
    {
        List<char> flattened = [];
        bool spaced = true;

        foreach (char character in text)
        {
            bool blank = char.IsWhiteSpace(character) || character == '/';

            if (blank && spaced)
                continue;

            flattened.Add(blank ? ' ' : character);
            spaced = blank;
        }

        return new string([.. flattened]).Trim();
    }

    // The summary's words with their layout removed, so two comments wrapped differently across
    // lines still count as the same sentence.
    private static string? GetSummary(MethodDeclarationSyntax method)
    {
        foreach (SyntaxTrivia trivia in method.GetLeadingTrivia())
        {
            if (trivia.GetStructure() is not DocumentationCommentTriviaSyntax documentation)
                continue;

            foreach (XmlNodeSyntax node in documentation.Content)
            {
                if (node is not XmlElementSyntax element)
                    continue;

                if (string.Equals(element.StartTag.Name.LocalName.ValueText, b: "summary", StringComparison.Ordinal))
                    return Flatten(element.Content.ToString());
            }
        }

        return null;
    }
}
