using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Documentation;

/// <summary>
/// Requires the text of a public or protected member's documentation to be written as sentences that
/// end with a full stop, as the .NET documentation is.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocumentationSentenceAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = DocumentationTags.SentenceRuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Documentation text ends with a full stop",
        messageFormat: "End the text of this <{0}> element with a full stop",
        category: "Documentation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration, DocumentationTags.DeclarationKinds);
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        DocumentedMember? member = DocumentationTags.Find(context.Node, context.SemanticModel, context.CancellationToken);

        if (member is null)
            return;

        foreach (XmlNodeSyntax node in member.Elements)
        {
            if (node is not XmlElementSyntax element || !DocumentationTags.IsDescription(element))
                continue;

            // The report sits where the full stop goes, which is also all the fix needs to know.
            if (DocumentationTags.FindUnpunctuatedEnd(element) is int end)
            {
                Location location = Location.Create(element.SyntaxTree, new TextSpan(end, length: 0));

                context.ReportDiagnostic(Diagnostic.Create(Rule, location, DocumentationTags.GetName(element)));
            }
        }
    }
}
