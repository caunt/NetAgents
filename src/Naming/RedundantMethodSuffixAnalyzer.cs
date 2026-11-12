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
/// Rejects calls whose method name ends with a word its first argument already says, such as
/// <c>UpdateValue(value: 1)</c> or <c>FromJob(jobAbc)</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantMethodSuffixAnalyzer() : PolicyAnalyzer(Rule, analyzesRazorCode: true)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0031";

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Method names must not repeat their first argument",
        messageFormat: "Rename '{0}' so it does not end with '{1}', which its first argument '{2}' already says",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    private static readonly Regex WordPattern = new(pattern: "[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+", RegexOptions.CultureInvariant);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.ArgumentList.Arguments.Count == 0 || invocation.ContainsDiagnostics)
            return;

        SyntaxToken nameToken = invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier,
            SimpleNameSyntax simple => simple.Identifier,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier,
            _ => default,
        };

        if (nameToken.IsKind(SyntaxKind.None))
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        IMethodSymbol declared = (method.ReducedFrom ?? method).OriginalDefinition;

        if (!IsRenamable(declared, context.Compilation))
            return;

        string? suffix = GetSuffix(declared.Name);

        if (suffix is null)
            return;

        ArgumentSyntax first = invocation.ArgumentList.Arguments[index: 0];

        foreach (string name in GetArgumentNames(first))
        {
            if (GetWords(name).Contains(suffix, StringComparer.OrdinalIgnoreCase))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, nameToken.GetLocation(), declared.Name, suffix, name));

                return;
            }
        }
    }

    // The names a reader sees for the first argument: the parameter it is passed to by name, and the
    // variable, field, or property it is read from.
    private static IEnumerable<string> GetArgumentNames(ArgumentSyntax argument)
    {
        if (argument.NameColon is not null)
            yield return argument.NameColon.Name.Identifier.ValueText;

        ExpressionSyntax expression = argument.Expression;

        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        SyntaxToken identifier = expression switch
        {
            SimpleNameSyntax simple => simple.Identifier,
            MemberAccessExpressionSyntax access => access.Name.Identifier,
            DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation } => designation.Identifier,
            _ => default,
        };

        if (!identifier.IsKind(SyntaxKind.None))
            yield return identifier.ValueText;
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

    // Only a method this project declares can be renamed by its authors: a framework or package
    // method keeps its name, and so does a member whose name another assembly's contract fixes.
    private static bool IsRenamable(IMethodSymbol method, Compilation compilation)
    {
        bool declaredHere = SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly);

        if (!declaredHere || method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0)
            return false;

        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction))
            return false;

        if (method.ContainingType is not INamedTypeSymbol type)
            return true;

        foreach (INamedTypeSymbol contract in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(contract.ContainingAssembly, compilation.Assembly))
                continue;

            foreach (ISymbol member in contract.GetMembers(method.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), method))
                    return false;
            }
        }

        return true;
    }
}
