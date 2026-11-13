using System;
using System.Collections.Immutable;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Configuration;

/// <summary>
/// Rejects suppressions of the rules the build requires: NetAgents rules, .NET code analysis rules, code style
/// rules, and the missing documentation warning.
/// </summary>
/// <remarks>
/// <para>
/// The build already refuses <c>NoWarn</c>, <c>WarningsNotAsErrors</c>, and configuration overrides for these
/// rules, and NetAgents rules ignore pragmas. What remained were the suppressions written into the code:
/// </para>
/// <code>
/// [SuppressMessage("Design", "CA1031")]          // reported
/// [assembly: SuppressMessage("Style", "IDE0055")] // reported
/// #pragma warning disable CA2000                 // reported
/// #pragma warning disable                        // reported: it silences every warning
/// </code>
/// <para>
/// Each of these rules can be satisfied by changing the code, so the message asks for that. Suppressions of other
/// analyzers, such as the analyzer authoring rules, stay available.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RuleSuppressionAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0043";

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Required rules must not be suppressed",
        messageFormat: "Remove this suppression of {0} and change the code so the rule holds: NetAgents, code analysis, style, and documentation rules are part of the build, and every one of them has a shape it accepts. Read the rule's own message for what it asks; where a framework seems to force the violation, look for the form the rule allows, such as ReadOnlyMemory<byte> instead of a byte[] property or a filtered catch instead of catch (Exception).",
        category: "Configuration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
        context.RegisterSyntaxTreeAction(AnalyzePragmas);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        AttributeSyntax attribute = (AttributeSyntax)context.Node;
        string name = attribute.Name.ToString();

        // Binding waits for a name that could be a suppression, since most attributes are not.
        if (!name.Contains(value: "SuppressMessage") || attribute.ArgumentList is null)
            return;

        ISymbol? constructor = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol;
        string type = constructor?.ContainingType.ToDisplayString() ?? string.Empty;
        bool suppression = type is "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute" or "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute";

        // Only the rule the suppression names decides, so another analyzer's suppression passes.
        ExpressionSyntax? checkId = suppression ? GetCheckId(attribute.ArgumentList) : null;
        string value = checkId is null ? string.Empty : ReadCheckId(attribute, checkId, context.SemanticModel, context.CancellationToken);

        if (IsRequired(value))
            context.ReportDiagnostic(Diagnostic.Create(Rule, attribute.GetLocation(), $"'{GetIdentifier(value)}'"));
    }

    private static void AnalyzePragmas(SyntaxTreeAnalysisContext context)
    {
        SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);

        foreach (SyntaxTrivia trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.GetStructure() is not PragmaWarningDirectiveTriviaSyntax pragma)
                continue;

            if (!pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
                continue;

            // Without codes the pragma silences every warning, the required ones among them.
            if (pragma.ErrorCodes.Count == 0)
                context.ReportDiagnostic(Diagnostic.Create(Rule, pragma.GetLocation(), messageArgs: ["every warning"]));

            foreach (ExpressionSyntax code in pragma.ErrorCodes)
            {
                string value = code.ToString();

                if (IsRequired(value))
                    context.ReportDiagnostic(Diagnostic.Create(Rule, code.GetLocation(), $"'{value}'"));
            }
        }
    }

    // The attributes the compiler built for the declaration, or the assembly or module, that the attribute is written on.
    private static ImmutableArray<AttributeData> GetAppliedAttributes(AttributeSyntax attribute, SemanticModel model, CancellationToken cancellationToken)
    {
        if (attribute.Parent is not AttributeListSyntax list)
            return [];

        string target = list.Target?.Identifier.ValueText ?? string.Empty;

        if (target is "assembly" or "module")
            return target == "assembly" ? model.Compilation.Assembly.GetAttributes() : model.Compilation.SourceModule.GetAttributes();

        SyntaxNode? owner = list.Parent is BaseFieldDeclarationSyntax { Declaration.Variables.Count: > 0 } field ? field.Declaration.Variables[index: 0] : list.Parent;
        ISymbol? symbol = owner is null ? null : model.GetDeclaredSymbol(owner, cancellationToken);

        return symbol is IMethodSymbol method && target == "return" ? method.GetReturnTypeAttributes() : symbol?.GetAttributes() ?? [];
    }

    // The rule identifier is the second argument, positional or named checkId.
    private static ExpressionSyntax? GetCheckId(AttributeArgumentListSyntax arguments)
    {
        int position = 0;

        foreach (AttributeArgumentSyntax argument in arguments.Arguments)
        {
            if (argument.NameEquals is not null)
                continue;

            if (argument.NameColon is not null)
            {
                if (argument.NameColon.Name.Identifier.ValueText == "checkId")
                    return argument.Expression;

                continue;
            }

            if (position++ == 1)
                return argument.Expression;
        }

        return null;
    }

    // A check id may carry the rule title after a colon, as in "CA1031:Do not catch general exception types".
    private static string GetIdentifier(string checkId)
    {
        int colon = checkId.IndexOf(value: ':');

        return (colon < 0 ? checkId : checkId.Substring(startIndex: 0, colon)).Trim();
    }

    private static bool IsRequired(string checkId)
    {
        string identifier = GetIdentifier(checkId);

        if (identifier is "1591" or "CS1591")
            return true;

        foreach (string prefix in new[] { "NETAGENTS", "IDE", "CA" })
        {
            bool numbered = identifier.Length > prefix.Length && char.IsDigit(identifier[prefix.Length]);

            if (numbered && identifier.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // A string literal is read as written. A named constant is read from the attribute the compiler built, which has
    // already evaluated it, so a constant declared in another file is read too.
    private static string ReadCheckId(AttributeSyntax attribute, ExpressionSyntax checkId, SemanticModel model, CancellationToken cancellationToken)
    {
        if (checkId is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            return literal.Token.ValueText;

        foreach (AttributeData data in GetAppliedAttributes(attribute, model, cancellationToken))
        {
            if (data.ApplicationSyntaxReference?.Span != attribute.Span || data.AttributeConstructor is not IMethodSymbol constructor)
                continue;

            for (int index = 0; index < constructor.Parameters.Length && index < data.ConstructorArguments.Length; index++)
            {
                if (constructor.Parameters[index].Name == "checkId")
                    return data.ConstructorArguments[index].ToCSharpString().Trim(trimChars: ['"']);
            }
        }

        return string.Empty;
    }
}
