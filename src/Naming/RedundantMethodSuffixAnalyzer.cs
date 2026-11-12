using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Naming;

/// <summary>
/// Rejects method declarations whose name ends with a word their first parameter's name already says,
/// such as <c>UpdateValue(int value)</c>, which should be <c>Update(int value)</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantMethodSuffixAnalyzer() : PolicyAnalyzer(Rule, analyzesRazorCode: true)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0031";

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Method names must not repeat their first parameter",
        messageFormat: "Rename '{0}' so it does not end with '{1}', which its first parameter '{2}' already says",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    private static readonly Regex WordPattern = new(pattern: "[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+", RegexOptions.CultureInvariant);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration, SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement);
    }

    // Only the declaration is checked: the name of the method and the name of its first parameter. What
    // a caller passes, or what its variables are called, says nothing about the method's name.
    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        SyntaxToken identifier = context.Node switch
        {
            MethodDeclarationSyntax declaration => declaration.Identifier,
            LocalFunctionStatementSyntax function => function.Identifier,
            _ => default,
        };

        ParameterListSyntax? parameters = context.Node switch
        {
            MethodDeclarationSyntax declaration => declaration.ParameterList,
            LocalFunctionStatementSyntax function => function.ParameterList,
            _ => null,
        };

        if (parameters is null || parameters.Parameters.Count == 0)
            return;

        string? suffix = GetSuffix(identifier.ValueText);
        string parameter = parameters.Parameters[index: 0].Identifier.ValueText;

        if (suffix is null || !GetWords(parameter).Contains(suffix, StringComparer.OrdinalIgnoreCase))
            return;

        // Binding waits until the names repeat, since most declarations never get this far.
        ISymbol? declared = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken);

        if (declared is not IMethodSymbol method || !IsRenamable(method))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(), identifier.ValueText, suffix, parameter));
    }

    // The last word of the method's name, ignoring an Async suffix, or nothing for a one-word name,
    // which would be left without a name once the word went.
    private static string? GetSuffix(string name)
    {
        if (name.Length > "Async".Length && name.EndsWith(value: "Async", StringComparison.Ordinal))
            name = name.Substring(startIndex: 0, name.Length - "Async".Length);

        List<string> words = GetWords(name);

        return words.Count < 2 ? null : words[words.Count - 1];
    }

    private static List<string> GetWords(string name)
    {
        return [.. WordPattern.Matches(name.TrimStart(trimChars: ['_', '@'])).Cast<Match>().Select(static match => match.Value)];
    }

    // A method whose name comes from somewhere else is not renamed where it is implemented: an override
    // or an interface implementation follows the member it implements, which is reported where it is
    // declared or cannot be renamed when another assembly declares it, a partial method's implementation
    // follows its definition, and an extern method's name can be the native entry point.
    private static bool IsRenamable(IMethodSymbol method)
    {
        bool inherited = method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0;

        if (inherited || method.IsExtern || method.PartialDefinitionPart is not null)
            return false;

        if (method.MethodKind is MethodKind.LocalFunction || method.ContainingType is not INamedTypeSymbol type)
            return true;

        foreach (INamedTypeSymbol contract in type.AllInterfaces)
        {
            foreach (ISymbol member in contract.GetMembers(method.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), method))
                    return false;
            }
        }

        return true;
    }
}
