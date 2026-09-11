using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Requires named arguments for inline literals and default values.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitLiteralArgumentNameAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0009";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Literal arguments need explicit parameter names",
        messageFormat: "Name the parameter when passing an inline literal argument",
        category: "Readability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeArgument, SyntaxKind.Argument);
    }

    private static void AnalyzeArgument(SyntaxNodeAnalysisContext context)
    {
        ArgumentSyntax argument = (ArgumentSyntax)context.Node;
        ExpressionSyntax expression = argument.Expression;

        while (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
            expression = parenthesizedExpression.Expression;

        if (expression is PrefixUnaryExpressionSyntax prefixExpression)
            expression = prefixExpression.Operand;

        if (argument.NameColon is not null || expression is not LiteralExpressionSyntax and not DefaultExpressionSyntax)
            return;

        IParameterSymbol? parameter = ResolveParameter(argument, context);

        if (parameter is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, argument.GetLocation()));
    }

    private static IParameterSymbol? ResolveParameter(ArgumentSyntax argument, SyntaxNodeAnalysisContext context)
    {
        if (context.SemanticModel.GetOperation(argument, context.CancellationToken) is IArgumentOperation operation)
            return operation.Parameter;

        if (argument.Parent is not BaseArgumentListSyntax argumentList || argumentList.Parent is null)
            return null;

        ISymbol? invokedSymbol = context.SemanticModel.GetSymbolInfo(argumentList.Parent, context.CancellationToken).Symbol;

        ImmutableArray<IParameterSymbol> parameters = invokedSymbol switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol property => property.Parameters,
            _ => [],
        };

        int argumentIndex = argumentList.Arguments.IndexOf(argument);

        return argumentIndex < parameters.Length
            ? parameters[argumentIndex]
            : parameters.Length > 0 && parameters[parameters.Length - 1].IsParams
            ? parameters[parameters.Length - 1]
            : null;
    }
}
