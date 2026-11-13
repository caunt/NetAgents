using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Requires a lambda, anonymous method, or method group to be assigned directly where its delegate type is written out,
/// rather than wrapped in a delegate constructor.
/// </summary>
/// <remarks>
/// <c>ConsoleCancelEventHandler handler = new((sender, eventArguments) => …);</c> becomes
/// <c>ConsoleCancelEventHandler handler = (sender, eventArguments) => …;</c>; the declared type already makes the
/// conversion, so the constructor only adds noise.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DelegateCreationAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = DelegateCreation.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Lambdas and method groups must not be wrapped in a delegate constructor",
        messageFormat: "Assign the lambda or method group directly: the declared type already converts it to this delegate, so the constructor around it only adds noise",
        category: "Readability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeCreation, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeCreation(SyntaxNodeAnalysisContext context)
    {
        BaseObjectCreationExpressionSyntax creation = (BaseObjectCreationExpressionSyntax)context.Node;

        if (DelegateCreation.GetWrapped(creation, context.SemanticModel, context.CancellationToken) is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation()));
    }
}
