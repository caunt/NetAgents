using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Ordering;

/// <summary>
/// Requires the members of every type to be in order and grouped, with blank lines only between groups.
/// </summary>
/// <remarks>
/// <para>
/// Members sort by kind (fields, constructors, finalizer, events, nested enums and interfaces, properties, indexers,
/// operators, methods, nested types, delegates), then visibility, then constants, static, and instance members, then
/// members fixed once construction ends before settable ones, then name. Initializers that run code or read other
/// members keep their written order, and so do the fields and properties of structs, explicit layouts, and types
/// that serializers read.
/// </para>
/// <para>
/// Single-line fields, properties, and events of one bucket form a group with no blank line inside; one blank line
/// separates groups, a comment line opens a new one, and every other member stands alone:
/// </para>
/// <code>
/// public Memory&lt;byte&gt; LocalPrivateKey { get; init; } = localPrivateKey;
/// public Memory&lt;byte&gt; SessionKey { get; init; } = sessionKey;
///
/// public Nonce? ReceiveNonce { get; set; }
/// public Nonce? SendNonce { get; set; }
/// </code>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MemberOrderingAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = MemberOrdering.RuleIdentifier;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Type members must be ordered and grouped",
        messageFormat: "Organize the members of this type: sort them by kind, visibility, constant, static or instance, fixed at construction before settable, then name, and separate groups with one blank line and none inside a group. The fix does both and keeps initializers that run code or read other members in their written order.",
        category: "Ordering",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeOrder,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.InterfaceDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration);
    }

    private static void AnalyzeOrder(SyntaxNodeAnalysisContext context)
    {
        TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.Node;

        if (!MemberOrdering.IsOrdered(declaration))
            context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation()));
    }
}
