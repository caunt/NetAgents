using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;
using NetAgents.Analyzers.Ordering;

namespace NetAgents.Analyzers.StatementGroups;

/// <summary>
/// Limits each group of statements or members to four, so a longer run is split by comments saying what each part
/// is for.
/// </summary>
/// <remarks>
/// Groups are the runs NETAGENTS0039 separates with blank lines between statements, and the runs of single-line
/// fields, properties, and events NETAGENTS0022 keeps together between members; a blank line does not end one, and a
/// comment line does. Runs of argument checks, and constructor runs that store one parameter per line, are exempt: a comment
/// between them could only restate the signature. There is no automatic fix, since only the author knows where
/// the purpose of the code changes and what to say about it.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatementGroupSizeAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = StatementGrouping.SizeRuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Groups of statements or members must hold at most four",
        messageFormat: "These {0} {1} run together as one group, and a group may hold at most 4. Split it where its purpose changes by putting a comment line above each part, so that every part has 4 or fewer. A comment line starts a new group; a blank line does not, because the formatter removes it. Write each comment for a person reading the code: one short sentence that says what the part is for or why it is needed (the step it performs, the state it holds, the rule it enforces), which the code alone does not tell them. Good: \"// Clients older than 2.0 send the length first.\" Bad: \"// Read the length.\" Do not restate the code (\"// Declare variables\", \"// Fields\"), do not number the parts, and do not split where nothing changes. When one part is a separate step, a well-named method can hold it instead; do not shuffle statements or invent helpers only to get under the limit." + PolicyMessages.ChangeTheCode,
        category: "Readability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSemanticModelAction(AnalyzeGroups);

        context.RegisterSyntaxNodeAction(AnalyzeMembers,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.InterfaceDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration);
    }

    private static void AnalyzeGroups(SemanticModelAnalysisContext context)
    {
        SyntaxTree tree = context.SemanticModel.SyntaxTree;
        SyntaxNode root = tree.GetRoot(context.CancellationToken);
        SourceText source = tree.GetText(context.CancellationToken);

        foreach (StatementGroup group in StatementGrouping.GetLargeGroups(root, context.SemanticModel, source, context.CancellationToken))
        {
            Location location = Location.Create(tree, TextSpan.FromBounds(group.First.SpanStart, group.Last.Span.End));
            string count = group.Count.ToString(CultureInfo.InvariantCulture);

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, count, Describe(group.Kind)));
        }
    }

    private static void AnalyzeMembers(SyntaxNodeAnalysisContext context)
    {
        foreach (MemberGroup group in MemberSpacing.GetLargeGroups((TypeDeclarationSyntax)context.Node))
        {
            Location location = Location.Create(context.Node.SyntaxTree, TextSpan.FromBounds(group.First.SpanStart, group.Last.Span.End));
            string count = group.Count.ToString(CultureInfo.InvariantCulture);

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, count, group.Description));
        }
    }

    private static string Describe(StatementKind kind)
    {
        return kind switch
        {
            StatementKind.Alone => "statements",
            StatementKind.Declaration => "declarations",
            StatementKind.AwaitedDeclaration => "declarations of awaited results",
            StatementKind.Assignment => "assignments",
            StatementKind.AwaitedAssignment => "assignments of awaited results",
            StatementKind.EventSubscription => "event subscriptions",
            StatementKind.Call => "calls",
            StatementKind.Await => "await statements",
            StatementKind.ArgumentCheck => "argument checks",
            _ => "statements",
        };
    }
}
