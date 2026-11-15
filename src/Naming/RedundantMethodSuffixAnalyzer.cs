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
/// Rejects method declarations whose name ends with their first parameter's whole name, such as
/// <c>UpdateValue(int value)</c>, which should be <c>Update(int value)</c>, or
/// <c>GetAppStoreId(string appStoreId)</c>, which should be <c>Get(string appStoreId)</c>.
/// </summary>
/// <remarks>
/// Names are compared word by word, ignoring case and an <c>Async</c> suffix. A name that shares only some of
/// the parameter's words names something else and is accepted: <c>GetApp(string appStoreId)</c> gets an app,
/// and <c>GetFileId(string appStoreId)</c> gets a file's id, not the store's id the parameter holds.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantMethodSuffixAnalyzer() : PolicyAnalyzer(Rule, analyzesRazorCode: true)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0031";

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Method names must not repeat their first parameter",
        messageFormat: "Rename '{0}' so it does not end with '{1}', which its first parameter '{2}' already says: drop those words from the method name, so a call reads like cart.Add(item). Renaming only the parameter to get past this leaves the call repeating itself." + PolicyMessages.ChangeTheCode,
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

        string parameter = parameters.Parameters[index: 0].Identifier.ValueText;
        string? repeated = GetRepeatedWords(identifier.ValueText, parameter);

        if (repeated is null)
            return;

        // Binding waits until the names repeat, since most declarations never get this far.
        ISymbol? declared = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken);

        if (declared is not IMethodSymbol method || !IsRenamable(method))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(), identifier.ValueText, repeated, parameter));
    }

    // The words that end the method's name, ignoring an Async suffix, when they spell the parameter's whole name,
    // or nothing otherwise. A name made of nothing but the parameter's words, a one-word name among them, is
    // accepted, since it would be left without a name once they went.
    private static string? GetRepeatedWords(string name, string parameter)
    {
        if (name.Length > "Async".Length && name.EndsWith(value: "Async", StringComparison.Ordinal))
            name = name.Substring(startIndex: 0, name.Length - "Async".Length);

        List<string> words = GetWords(name);
        List<string> repeated = GetWords(parameter);
        int start = words.Count - repeated.Count;

        if (repeated.Count == 0 || start < 1)
            return null;

        for (int index = 0; index < repeated.Count; index++)
        {
            if (!string.Equals(words[start + index], repeated[index], StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return string.Concat(words.GetRange(start, repeated.Count));
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
