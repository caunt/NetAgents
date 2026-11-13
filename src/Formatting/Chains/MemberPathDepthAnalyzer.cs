using System.Collections.Generic;
using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting.Chains;

/// <summary>
/// Rejects more than four property, field, or event accesses in a row, so the object a chain reaches for gets a name.
/// </summary>
/// <remarks>
/// <code>
/// string host = settings.Network.Proxy.Upstream.Address.Host;   // reported: five accesses deep
///
/// UpstreamEndpoint upstream = settings.Network.Proxy.Upstream;
/// string host = upstream.Address.Host;                           // accepted
/// </code>
/// <para>
/// A call between accesses starts the count again, and namespace and type qualifiers do not count. There is no
/// automatic fix: only the author knows which object deserves a name and what to call it.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MemberPathDepthAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = ChainLayout.PathRuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Member paths must not reach more than four members deep",
        messageFormat: "Name the object this reaches for: it goes {0} members deep, so store an intermediate value in a local named for what it is, as in UpstreamEndpoint upstream = settings.Network.Proxy.Upstream; and then read upstream.Address.Host." + PolicyMessages.ChangeTheCode,
        category: "Readability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzePath, SyntaxKind.InvocationExpression, SyntaxKind.SimpleMemberAccessExpression, SyntaxKind.ElementAccessExpression);
    }

    private static void AnalyzePath(SyntaxNodeAnalysisContext context)
    {
        ExpressionSyntax top = (ExpressionSyntax)context.Node;

        if (!ChainLayout.IsTop(top))
            return;

        List<ChainLink>? links = ChainLayout.GetLinks(top, context.SemanticModel, context.CancellationToken);
        int depth = links is null ? 0 : ChainLayout.GetLongestPath(links);

        if (depth > ChainLayout.MaximumLinks)
            context.ReportDiagnostic(Diagnostic.Create(Rule, top.GetLocation(), depth.ToString(CultureInfo.InvariantCulture)));
    }
}
