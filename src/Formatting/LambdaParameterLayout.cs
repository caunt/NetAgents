using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Formatting;

internal static class LambdaParameterLayout
{
    public const string RuleIdentifier = "NETAGENTS0026";

    // Parentheses are optional only around one bare parameter. An explicit type, an attribute, a
    // modifier, a default value, or an explicit return type each require them, so those stay written.
    public static bool IsViolation(ParenthesizedLambdaExpressionSyntax lambda)
    {
        if (lambda.ReturnType is not null || lambda.ParameterList.Parameters.Count != 1)
            return false;

        ParameterSyntax parameter = lambda.ParameterList.Parameters[index: 0];

        return parameter.Type is null && parameter.Default is null && parameter.AttributeLists.Count == 0 && parameter.Modifiers.Count == 0;
    }

    // Padding inside the parentheses only existed to space them, so it goes with them, while the
    // spacing that trails the closing parenthesis keeps the parameter apart from the arrow.
    public static SimpleLambdaExpressionSyntax Simplify(ParenthesizedLambdaExpressionSyntax lambda)
    {
        ParameterListSyntax parameters = lambda.ParameterList;
        ParameterSyntax parameter = parameters.Parameters[index: 0];

        SyntaxTrivia[] leading = [
            .. parameters.OpenParenToken.LeadingTrivia,
            .. Meaningful(parameters.OpenParenToken.TrailingTrivia),
            .. Meaningful(parameter.GetLeadingTrivia())];

        SyntaxTrivia[] trailing = [
            .. Meaningful(parameter.GetTrailingTrivia()),
            .. Meaningful(parameters.CloseParenToken.LeadingTrivia),
            .. parameters.CloseParenToken.TrailingTrivia];

        return SyntaxFactory.SimpleLambdaExpression(
            lambda.Modifiers,
            parameter.WithLeadingTrivia(leading).WithTrailingTrivia(trailing),
            lambda.ArrowToken,
            lambda.Block,
            lambda.ExpressionBody
        );
    }

    // Whitespace between the parentheses is padding; anything else is a comment worth keeping.
    private static SyntaxTriviaList Meaningful(SyntaxTriviaList trivia)
    {
        foreach (SyntaxTrivia item in trivia)
        {
            if (!item.IsKind(SyntaxKind.WhitespaceTrivia) && !item.IsKind(SyntaxKind.EndOfLineTrivia))
                return trivia;
        }

        return default;
    }
}
