using System;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting;

namespace NetAgents.Analyzers.StatementGroups;

/// <summary>
/// Decides which group each statement of one syntax tree joins.
/// </summary>
/// <param name="model">The semantic model of the tree, which tells events and argument checks apart.</param>
/// <param name="source">The text of the tree.</param>
/// <param name="cancellationToken">The token that cancels binding.</param>
internal sealed class StatementClassifier(SemanticModel model, SourceText source, CancellationToken cancellationToken)
{
    private readonly INamedTypeSymbol? _argumentException = model
        .Compilation
        .GetTypeByMetadataName(fullyQualifiedMetadataName: "System.ArgumentException");

    public StatementKind Classify(StatementSyntax statement)
    {
        // What NETAGENTS0016 already separates stands alone here too, so the two rules never disagree, and so does
        // every statement written over several lines, so two of them never share a group.
        return StatementSpacing.RequiresSeparation(statement, source) || SpansLines(statement)
            ? StatementKind.Alone
            : statement switch
            {
                LocalDeclarationStatementSyntax declaration => StoresAwaitedResults(declaration) ? StatementKind.AwaitedDeclaration : StatementKind.Declaration,
                ExpressionStatementSyntax expression => ClassifyEffect(expression.Expression),
                _ => StatementKind.Alone,
            };
    }

    // Every variable the declaration introduces takes the result of an await, as in string text = await reader.ReadToEndAsync();.
    private static bool StoresAwaitedResults(LocalDeclarationStatementSyntax declaration)
    {
        foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not AwaitExpressionSyntax)
                return false;
        }

        return true;
    }

    private StatementKind ClassifyEffect(ExpressionSyntax expression)
    {
        return expression switch
        {
            AwaitExpressionSyntax => StatementKind.Await,
            AssignmentExpressionSyntax { Right: AwaitExpressionSyntax } => StatementKind.AwaitedAssignment,
            AssignmentExpressionSyntax assignment => IsEventSubscription(assignment) ? StatementKind.EventSubscription : StatementKind.Assignment,
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax => StatementKind.Assignment,
            InvocationExpressionSyntax invocation when IsArgumentCheck(invocation) => StatementKind.ArgumentCheck,
            _ => StatementKind.Call,
        };
    }

    private bool IsArgumentCheck(InvocationExpressionSyntax invocation)
    {
        string name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => string.Empty,
        };

        // Binding waits for the name, since most calls are not argument checks.
        if (_argumentException is null || !name.StartsWith(value: "ThrowIf", StringComparison.Ordinal))
            return false;

        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol { IsStatic: true } method)
            return false;

        for (INamedTypeSymbol? type = method.ContainingType; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, _argumentException))
                return true;
        }

        return false;
    }

    private bool IsEventSubscription(AssignmentExpressionSyntax assignment)
    {
        return (assignment.IsKind(SyntaxKind.AddAssignmentExpression) || assignment.IsKind(SyntaxKind.SubtractAssignmentExpression)) && model
            .GetSymbolInfo(assignment.Left, cancellationToken)
            .Symbol is IEventSymbol;
    }

    private bool SpansLines(StatementSyntax statement)
    {
        return source.Lines.GetLineFromPosition(statement.SpanStart).LineNumber != source
            .Lines
            .GetLineFromPosition(statement.Span.End)
            .LineNumber;
    }
}
