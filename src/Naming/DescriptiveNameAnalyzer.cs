using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Naming;

/// <summary>
/// Requires descriptive identifiers without known abbreviations or acronyms.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DescriptiveNameAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0005";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Names must use full descriptive words",
        messageFormat: "Expand single-letter names, acronyms, and known abbreviations into full descriptive words",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    private static readonly Regex WordPattern = new(
        pattern: "[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+",
        options: RegexOptions.CultureInvariant);

    private static readonly ImmutableHashSet<string> AbbreviatedWords = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        items: [        "api", "args", "async", "auth", "cfg", "config", "ctx", "db", "doc", "docs", "dto", "dsp",
        "env", "html", "http", "https", "id", "ids", "info", "init", "io", "ip", "json", "max", "min",
        "msg", "num", "opts", "param", "params", "prev", "proc", "ptr", "req", "res", "sdr", "sql",
        "tcp", "temp", "tmp", "udp", "ui", "uri", "url", "utf", "utils", "var", "xml"]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration,
            SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration,
            SyntaxKind.RecordDeclaration, SyntaxKind.RecordStructDeclaration, SyntaxKind.EnumDeclaration,
            SyntaxKind.EnumMemberDeclaration, SyntaxKind.DelegateDeclaration, SyntaxKind.MethodDeclaration,
            SyntaxKind.LocalFunctionStatement, SyntaxKind.PropertyDeclaration, SyntaxKind.EventDeclaration,
            SyntaxKind.VariableDeclarator, SyntaxKind.Parameter, SyntaxKind.TypeParameter,
            SyntaxKind.ForEachStatement, SyntaxKind.CatchDeclaration, SyntaxKind.SingleVariableDesignation);
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        SyntaxToken identifier = context.Node switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Identifier,
            DelegateDeclarationSyntax declaration => declaration.Identifier,
            MethodDeclarationSyntax declaration => declaration.Identifier,
            LocalFunctionStatementSyntax declaration => declaration.Identifier,
            PropertyDeclarationSyntax declaration => declaration.Identifier,
            EventDeclarationSyntax declaration => declaration.Identifier,
            VariableDeclaratorSyntax declaration => declaration.Identifier,
            ParameterSyntax declaration => declaration.Identifier,
            TypeParameterSyntax declaration => declaration.Identifier,
            ForEachStatementSyntax declaration => declaration.Identifier,
            CatchDeclarationSyntax declaration => declaration.Identifier,
            SingleVariableDesignationSyntax declaration => declaration.Identifier,
            EnumMemberDeclarationSyntax declaration => declaration.Identifier,
            _ => default,
        };

        if (identifier.IsMissing || identifier.ValueText.Length == 0)
            return;

        // Interface/override signatures can be owned by a framework or another assembly.
        ISymbol? symbol = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken);

        if (symbol is
        { IsOverride: true }
            or IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 }
            or IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 })
            return;

        string name = identifier.ValueText.TrimStart(trimChars: ['_']);

        if ((context.Node is InterfaceDeclarationSyntax && name.StartsWith(value: "I", StringComparison.Ordinal))
            || (context.Node is TypeParameterSyntax && name.StartsWith(value: "T", StringComparison.Ordinal)))
            name = name.Substring(startIndex: 1);

        bool invalidName = name.Length < 2;

        foreach (Match wordMatch in WordPattern.Matches(name))
        {
            string word = wordMatch.Value;

            if (AbbreviatedWords.Contains(word)
                || (word.Length > 1 && char.IsLetter(word[index: 0]) && word.All(static character => !char.IsLetter(character) || char.IsUpper(character))))
                invalidName = true;
        }

        if (invalidName)
            context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation()));
    }
}
