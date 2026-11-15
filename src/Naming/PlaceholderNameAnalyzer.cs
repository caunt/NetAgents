using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Naming;

/// <summary>
/// Rejects local names built only from placeholder words, such as <c>result</c>, <c>flag</c>, <c>temp</c>, or
/// <c>isConditionMet</c>, which say that a value exists but not what it is.
/// </summary>
/// <remarks>
/// A name is reported when every word in it is one of <c>is</c>, <c>condition</c>, <c>met</c>, <c>result</c>,
/// <c>flag</c>, <c>temp</c>, or a number, so <c>result2</c> and <c>tempResult</c> are reported while
/// <c>parseResult</c> and <c>hasNoVersions</c> are not. Local variables, <c>foreach</c> and <c>catch</c> variables,
/// and variables declared in patterns, <c>out</c> arguments, and deconstructions are covered; parameters, fields, and
/// properties are not.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PlaceholderNameAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0046";

    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase) { "condition", "flag", "result", "temp" };

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Locals must not be named with placeholder words",
        messageFormat: "Rename '{0}' to say what the value is or means here, such as hasNoVersions or selectedApp; placeholder words like condition, result, flag, and temp only say that a value exists." + PolicyMessages.ChangeTheCode,
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    // Words that join a placeholder without naming anything, as in isConditionMet.
    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase) { "is", "met" };
    private static readonly Regex WordPattern = new(pattern: "[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+", RegexOptions.CultureInvariant);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeVariable, SyntaxKind.VariableDeclarator);
        context.RegisterSyntaxNodeAction(AnalyzeDesignation, SyntaxKind.SingleVariableDesignation);
        context.RegisterSyntaxNodeAction(AnalyzeLoop, SyntaxKind.ForEachStatement);
        context.RegisterSyntaxNodeAction(AnalyzeCatch, SyntaxKind.CatchDeclaration);
    }

    private static void AnalyzeCatch(SyntaxNodeAnalysisContext context)
    {
        Report(context, ((CatchDeclarationSyntax)context.Node).Identifier);
    }

    private static void AnalyzeDesignation(SyntaxNodeAnalysisContext context)
    {
        Report(context, ((SingleVariableDesignationSyntax)context.Node).Identifier);
    }

    private static void AnalyzeLoop(SyntaxNodeAnalysisContext context)
    {
        Report(context, ((ForEachStatementSyntax)context.Node).Identifier);
    }

    // Fields and events declare variables too; only those declared inside a body are locals.
    private static void AnalyzeVariable(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.Parent?.Parent is not BaseFieldDeclarationSyntax)
            Report(context, ((VariableDeclaratorSyntax)context.Node).Identifier);
    }

    private static bool IsPlaceholder(string name)
    {
        bool named = false;
        bool placeholder = false;

        foreach (Match word in WordPattern.Matches(name.TrimStart(trimChars: ['_', '@'])))
        {
            named = true;

            if (Placeholders.Contains(word.Value))
                placeholder = true;
            else if (!Fillers.Contains(word.Value) && !char.IsDigit(word.Value[index: 0]))
                return false;
        }

        return named && placeholder;
    }

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxToken identifier)
    {
        if (IsPlaceholder(identifier.ValueText))
            context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(), identifier.ValueText));
    }
}
