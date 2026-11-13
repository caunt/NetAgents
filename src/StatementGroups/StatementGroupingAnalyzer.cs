using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.StatementGroups;

/// <summary>
/// Requires the statements of every block to sit in groups of one kind, one blank line between groups and none
/// inside them.
/// </summary>
/// <remarks>
/// <para>
/// Declarations, assignments, event subscriptions, calls, awaits, and argument checks each form groups of their
/// own, and declarations and assignments that store an awaited result form two more. A comment line opens a new
/// group. A statement written over several lines stands alone whatever its kind, and so does every other statement,
/// as NETAGENTS0016 already keeps control flow and multiline declarations. Blank lines the author placed elsewhere are removed:
/// </para>
/// <code>
/// ArgumentNullException.ThrowIfNull(cart);
///
/// Order order = new(cart.CustomerId);
/// decimal total = 0;
///
/// order.Status = OrderStatus.Pending;
///
/// await _repository.SaveAsync(order, cancellationToken);
///
/// _logger.LogInformation(message: "Placed order {OrderId}", order.Id);
/// </code>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatementGroupingAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = StatementGrouping.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Statements must be grouped by kind",
        messageFormat: "Separate groups of statements with one blank line and keep none inside a group: declarations, declarations of awaited results, assignments, assignments of awaited results, event subscriptions, calls, await statements, and argument checks each form groups of their own, a comment line opens a new group, and a statement written over several lines or of any other kind stands alone",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSemanticModelAction(AnalyzeGroups);
    }

    private static void AnalyzeGroups(SemanticModelAnalysisContext context)
    {
        SyntaxTree tree = context.SemanticModel.SyntaxTree;
        SyntaxNode root = tree.GetRoot(context.CancellationToken);
        SourceText source = tree.GetText(context.CancellationToken);

        foreach (KeyValuePair<TextSpan, TextChange> change in StatementGrouping.GetChanges(root, context.SemanticModel, source, context.CancellationToken))
            context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(tree, change.Key)));
    }
}
