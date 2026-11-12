using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.TypeSafety;

namespace NetAgents.Analyzers.CodeFixes.TypeSafety;

/// <summary>
/// Rewrites numeric casts and implicit numeric conversions into the generic math factory that keeps
/// their behavior: <c>(int)value</c> becomes <c>int.CreateTruncating(value)</c>, and a compound assignment
/// narrowing its result becomes a plain assignment: <c>flags |= bit</c> on bytes becomes
/// <c>flags = byte.CreateTruncating(int.CreateChecked(flags) | int.CreateChecked(bit))</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NumericConversionCodeFixProvider))]
[Shared]
public sealed class NumericConversionCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [NumericConversion.RuleIdentifier];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return FixAllProvider.Create(static async (context, document, diagnostics) =>
            await Convert(document, diagnostics, context.CancellationToken).ConfigureAwait(continueOnCapturedContext: false));
    }

    /// <inheritdoc />
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            // A target that could change when read twice keeps its compound form for its author to rewrite.
            if (diagnostic.Properties.ContainsKey(NumericConversion.ManualProperty))
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Convert with a numeric factory method",
                    cancellationToken => Convert(context.Document, diagnostics: [diagnostic], cancellationToken),
                    nameof(NumericConversionCodeFixProvider)),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> Convert(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (root is null || model is null)
            return document;

        Dictionary<SyntaxNode, List<Replacement>> replacements = [];
        Dictionary<SyntaxNode, CompoundReplacement> compounds = [];

        foreach (Diagnostic diagnostic in diagnostics)
        {
            SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            // An assignment keeps any parentheses around it, since it may be the operand of something larger.
            if (node is AssignmentExpressionSyntax assignment)
            {
                if (!compounds.ContainsKey(assignment) && PlanCompound(assignment, model, cancellationToken) is CompoundReplacement compound)
                    compounds.Add(assignment, compound);

                continue;
            }

            // The call the fix writes needs no parentheses, so it replaces any that enclose the conversion.
            SyntaxNode target = node;

            while (target.Parent is ParenthesizedExpressionSyntax parenthesized)
                target = parenthesized;

            if (!replacements.ContainsKey(target) && Plan(node, model, cancellationToken) is { Count: > 0 } steps)
                replacements.Add(target, steps);
        }

        if (replacements.Count == 0 && compounds.Count == 0)
            return document;

        // Nested conversions are rewritten inside out, so an outer one wraps the call its operand became,
        // and an assignment is rebuilt from the operands its inner conversions already turned into calls.
        SyntaxNode rewritten = root.ReplaceNodes(nodes: [.. replacements.Keys, .. compounds.Keys],
            (original, current) => compounds.TryGetValue(original, out CompoundReplacement? compound)
                ? RewriteCompound(current, compound)
                : Rewrite(current, replacements[original]));

        return document.WithSyntaxRoot(rewritten);
    }

    // A value that is not a single operand keeps its grouping once it becomes the right side of an operator.
    private static bool NeedsParentheses(ExpressionSyntax value)
    {
        return value is not (IdentifierNameSyntax or LiteralExpressionSyntax or InvocationExpressionSyntax or MemberAccessExpressionSyntax
            or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or CastExpressionSyntax or PrefixUnaryExpressionSyntax
            or PostfixUnaryExpressionSyntax or CheckedExpressionSyntax or DefaultExpressionSyntax);
    }

    // The conversions the diagnostic names, innermost first: the cast itself, or the implicit conversion
    // applied to the operand it was reported on, followed by any implicit conversion of a cast's result,
    // since all of them share that syntax.
    private static List<Replacement> Plan(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        List<Replacement> steps = [];

        for (IOperation? current = model.GetOperation(node, cancellationToken); current is not null && current.Syntax == node; current = current.Parent)
        {
            if (current is not IConversionOperation conversion)
                continue;

            if (NumericConversion.GetFactory(conversion, model, cancellationToken) is not string factory)
                continue;

            string receiver = !conversion.IsImplicit && node is CastExpressionSyntax cast
                ? cast.Type.ToString()
                : conversion.Type?.ToMinimalDisplayString(model, node.SpanStart) ?? string.Empty;

            if (receiver.Length == 0)
                return [];

            steps.Add(new Replacement(factory, receiver, !conversion.IsImplicit));
        }

        return steps;
    }

    private static CompoundReplacement? PlanCompound(AssignmentExpressionSyntax assignment, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetOperation(assignment, cancellationToken) is not ICompoundAssignmentOperation compound)
            return null;

        CompoundNarrowing? narrowing = NumericConversion.GetCompoundNarrowing(compound);

        if (narrowing is null || !narrowing.Rewritable)
            return null;

        string target = narrowing.Target.ToMinimalDisplayString(model, assignment.SpanStart);
        string operand = model.Compilation.GetSpecialType(SpecialType.System_Int32).ToMinimalDisplayString(model, assignment.SpanStart);

        return new CompoundReplacement(target, narrowing.Factory, operand, narrowing.WidensValue);
    }

    private static ExpressionSyntax Rewrite(SyntaxNode current, List<Replacement> steps)
    {
        ExpressionSyntax expression = (ExpressionSyntax)current;

        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        foreach (Replacement step in steps)
        {
            ExpressionSyntax value = step.Written && expression is CastExpressionSyntax cast ? cast.Expression : expression;

            while (value is ParenthesizedExpressionSyntax parenthesized)
                value = parenthesized.Expression;

            expression = SyntaxFactory.ParseExpression(step.Receiver + "." + step.Factory + "(" + value.WithoutTrivia().ToFullString() + ")");
        }

        return expression.WithTriviaFrom(current);
    }

    // The compound form reads its target once and writes it once, as does the plain assignment, so only a
    // target that reads the same the second time is rewritten. Both operands are widened in the open, as
    // C# widened them, and the result is narrowed by the factory that behaves as the hidden narrowing did.
    private static ExpressionSyntax RewriteCompound(SyntaxNode current, CompoundReplacement plan)
    {
        AssignmentExpressionSyntax assignment = (AssignmentExpressionSyntax)current;
        string operation = assignment.OperatorToken.Text.TrimEnd(trimChars: ['=']);
        string target = assignment.Left.WithoutTrivia().ToFullString();
        string value = assignment.Right.WithoutTrivia().ToFullString();

        string right = plan.WidensValue
            ? plan.Operand + ".CreateChecked(" + value + ")"
            : NeedsParentheses(assignment.Right) ? "(" + value + ")" : value;

        string text = target + " = " + plan.Target + "." + plan.Factory + "(" + plan.Operand + ".CreateChecked(" + target + ") " + operation + " " + right + ")";

        return SyntaxFactory.ParseExpression(text).WithTriviaFrom(current);
    }

    private sealed class CompoundReplacement(string target, string factory, string operand, bool widensValue)
    {
        public string Factory { get; } = factory;

        public string Operand { get; } = operand;

        public string Target { get; } = target;

        public bool WidensValue { get; } = widensValue;
    }

    private sealed class Replacement(string factory, string receiver, bool written)
    {
        public string Factory { get; } = factory;

        public string Receiver { get; } = receiver;

        public bool Written { get; } = written;
    }
}
