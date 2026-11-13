using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Formatting.Statements;

/// <summary>
/// Requires a simple statement that fits within 128 characters to be written on one line.
/// </summary>
/// <remarks>
/// <code>
/// AppStoreSearchResponse response = await appStoreClient
///     .SearchAsync(value, cancellationToken)
///     .ConfigureAwait(continueOnCapturedContext: false);
/// </code>
/// <para>
/// fits within 128 characters, so it becomes one line. Parentheses on lines of their own and chains broken one member
/// per line are joined the same way. A statement that does not fit keeps its line breaks, and one holding a lambda
/// with statements, a switch expression, a comment, or a multiline string is left to the rules for those.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OneLineStatementAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = OneLineStatement.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "A statement that fits on one line must be written on one line",
        messageFormat: "Write this statement on one line: it fits within 128 characters, so its line breaks only stretch a short statement over several lines",
        category: "Formatting",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeStatement,
            SyntaxKind.LocalDeclarationStatement,
            SyntaxKind.ExpressionStatement,
            SyntaxKind.ReturnStatement,
            SyntaxKind.ThrowStatement,
            SyntaxKind.YieldReturnStatement);
    }

    private static void AnalyzeStatement(SyntaxNodeAnalysisContext context)
    {
        StatementSyntax statement = (StatementSyntax)context.Node;

        if (OneLineStatement.IsJoinable(statement, statement.SyntaxTree.GetText(context.CancellationToken)))
            context.ReportDiagnostic(Diagnostic.Create(Rule, statement.GetFirstToken().GetLocation()));
    }
}
