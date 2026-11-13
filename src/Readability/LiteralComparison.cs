using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Finds <c>string.Equals</c> calls that compare a value with a string literal, which a constant pattern says directly.
/// </summary>
/// <remarks>
/// <para>
/// <c>string.Equals(value, "--help", StringComparison.Ordinal)</c> becomes <c>value is "--help"</c>, and
/// <c>!string.Equals(value, "--help")</c> becomes <c>value is not "--help"</c>. A constant pattern compares strings
/// ordinally and is <see langword="false"/> for <see langword="null"/>, exactly like the static <c>string.Equals</c>
/// overloads without a comparison or with <c>StringComparison.Ordinal</c>, so only those calls are rewritten; any other
/// comparison, such as <c>OrdinalIgnoreCase</c>, keeps its call. Several patterns on one value then read as
/// <c>value is "-h" or "--help"</c>.
/// </para>
/// <para>
/// Expression trees cannot hold patterns, so calls inside a lambda converted to one, or inside a query expression,
/// are left alone.
/// </para>
/// </remarks>
internal static class LiteralComparison
{
    public const string RuleIdentifier = "NETAGENTS0051";

    // The pattern that replaces the call, or the ! in front of it, or nothing when the call is no such comparison.
    public static ExpressionSyntax? GetPattern(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Equals" })
            return null;

        if (model.GetOperation(invocation, cancellationToken) is not IInvocationOperation call || !IsOrdinalEquals(call, model))
            return null;

        ExpressionSyntax? first = GetArgument(call, ordinal: 0);
        ExpressionSyntax? second = GetArgument(call, ordinal: 1);

        // Exactly one side is a string literal, and the other is the value it is compared with.
        bool firstLiteral = first is LiteralExpressionSyntax literalFirst && literalFirst.IsKind(SyntaxKind.StringLiteralExpression);
        bool secondLiteral = second is LiteralExpressionSyntax literalSecond && literalSecond.IsKind(SyntaxKind.StringLiteralExpression);

        if (first is null || second is null || firstLiteral == secondLiteral || IsInExpressionTree(invocation, model, cancellationToken))
            return null;

        ExpressionSyntax value = firstLiteral ? second : first;
        ExpressionSyntax literal = firstLiteral ? first : second;

        if (value is LiteralExpressionSyntax)
            return null;

        ExpressionSyntax replaced = GetReplaced(invocation);
        string subject = IsPrimary(value) ? value.WithoutTrivia().ToString() : "(" + value.WithoutTrivia().ToString() + ")";
        string pattern = subject + (replaced == invocation ? " is " : " is not ") + literal.WithoutTrivia().ToString();
        ExpressionSyntax parsed = SyntaxFactory.ParseExpression(NeedsParentheses(replaced) ? "(" + pattern + ")" : pattern);

        return parsed.ContainsDiagnostics ? null : parsed.WithTriviaFrom(replaced);
    }

    // The node the pattern replaces: the ! in front of the call, which the pattern takes as not, or the call itself.
    public static ExpressionSyntax GetReplaced(InvocationExpressionSyntax invocation)
    {
        return invocation.Parent is PrefixUnaryExpressionSyntax negation && negation.IsKind(SyntaxKind.LogicalNotExpression) ? negation : invocation;
    }

    private static ExpressionSyntax? GetArgument(IInvocationOperation call, int ordinal)
    {
        foreach (IArgumentOperation argument in call.Arguments)
        {
            if (argument.Parameter?.Ordinal == ordinal && argument.Syntax is ArgumentSyntax syntax)
                return syntax.Expression;
        }

        return null;
    }

    // A pattern cannot stand in an expression tree, which a lambda converted to Expression<T> builds, and which a query
    // expression may build for its clauses without showing it.
    private static bool IsInExpressionTree(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        INamedTypeSymbol? lambdaExpression = model.Compilation
            .GetTypeByMetadataName(fullyQualifiedMetadataName: "System.Linq.Expressions.LambdaExpression");

        foreach (SyntaxNode ancestor in invocation.Ancestors())
        {
            if (ancestor is QueryExpressionSyntax)
                return true;

            if (ancestor is not AnonymousFunctionExpressionSyntax function)
                continue;

            for (ITypeSymbol? type = model.GetTypeInfo(function, cancellationToken).ConvertedType; type is not null; type = type.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(type, lambdaExpression))
                    return true;
            }
        }

        return false;
    }

    // The static string.Equals(string, string), or string.Equals(string, string, StringComparison) given Ordinal.
    private static bool IsOrdinalEquals(IInvocationOperation call, SemanticModel model)
    {
        IMethodSymbol method = call.TargetMethod;

        if (!method.IsStatic || method.ContainingType.SpecialType != SpecialType.System_String || method.Name is not "Equals")
            return false;

        if (method.Parameters.Length == 2)
            return true;

        INamedTypeSymbol? comparisonType = model.Compilation
            .GetTypeByMetadataName(fullyQualifiedMetadataName: "System.StringComparison");

        foreach (IArgumentOperation argument in call.Arguments)
        {
            if (argument.Parameter?.Ordinal != 2)
                continue;

            return argument.Value is IFieldReferenceOperation { Field.Name: "Ordinal" } field
                && SymbolEqualityComparer.Default.Equals(field.Field.ContainingType, comparisonType);
        }

        return false;
    }

    // An expression that an is pattern can follow without parentheses, since nothing in it binds looser than is.
    private static bool IsPrimary(ExpressionSyntax value)
    {
        return value is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax
            or ParenthesizedExpressionSyntax or ThisExpressionSyntax or PostfixUnaryExpressionSyntax or ConditionalAccessExpressionSyntax
            or InterpolatedStringExpressionSyntax or CastExpressionSyntax;
    }

    // An is pattern binds tighter than && and || but looser than an equality or another operator around it, so it stands
    // bare only where the call stood as a whole condition, operand of && or ||, value, or argument.
    private static bool NeedsParentheses(ExpressionSyntax replaced)
    {
        return replaced.Parent switch
        {
            ParenthesizedExpressionSyntax or ArgumentSyntax or EqualsValueClauseSyntax or ArrowExpressionClauseSyntax or ReturnStatementSyntax => false,
            IfStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or WhenClauseSyntax or CatchFilterClauseSyntax => false,
            BinaryExpressionSyntax binary => !binary.IsKind(SyntaxKind.LogicalAndExpression) && !binary.IsKind(SyntaxKind.LogicalOrExpression),
            AssignmentExpressionSyntax assignment => assignment.Left == replaced,
            ConditionalExpressionSyntax conditional => conditional.Condition != replaced,
            LambdaExpressionSyntax lambda => lambda.ExpressionBody != replaced,
            _ => true,
        };
    }
}
