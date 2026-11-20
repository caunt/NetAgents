using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Readability;

/// <summary>
/// Finds an interpolated string passed through <c>string.Create(CultureInfo.InvariantCulture, …)</c> whose holes print
/// the same under every culture, which the string alone writes.
/// </summary>
/// <remarks>
/// <para>
/// <c>string.Create(CultureInfo.InvariantCulture, $"{name} is ready")</c> becomes <c>$"{name} is ready"</c>. The
/// culture only reaches a value that formats itself through it, so a call is found when every hole holds a string, a
/// <c>bool</c>, a <c>char</c>, an enumeration, or a <c>Guid</c>, nullable or not. A number, a date, or any other type
/// keeps the call, because it can print differently from one culture to the next.
/// </para>
/// <para>
/// An argument may bind differently once it is an interpolated string, since a method can take an interpolated string
/// handler, so the string must leave the call it is passed to bound to the same method. A call holding a comment
/// outside the string is left alone, because the comment would be lost.
/// </para>
/// </remarks>
internal static class InvariantInterpolation
{
    public const string RuleIdentifier = "NETAGENTS0056";

    // The interpolated string the call becomes, or nothing when it is no such call or the string alone could print or
    // bind differently.
    public static InterpolatedStringExpressionSyntax? GetUnwrapped(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken)
    {
        if (call.ArgumentList.Arguments.Count != 2 || call.ContainsDirectives || !IsNamedCreate(call.Expression))
            return null;

        if (model.GetSymbolInfo(call, cancellationToken).Symbol is not IMethodSymbol method || !IsFormattingCreate(method))
            return null;

        ArgumentSyntax? provider = GetArgument(call, method.Parameters[index: 0]);
        ArgumentSyntax? handler = GetArgument(call, method.Parameters[index: 1]);

        return provider is null || handler?.Expression is not InterpolatedStringExpressionSyntax text
            ? null
            : !IsInvariantCulture(provider.Expression, model, cancellationToken) || HasCommentOutside(call, text)
                ? null
                : IgnoresCulture(text, model, cancellationToken) && LiteralConcatenation.KeepsBinding(call, text, model, cancellationToken)
                    ? text.WithTriviaFrom(call)
                    : null;
    }

    private static ArgumentSyntax? GetArgument(InvocationExpressionSyntax call, IParameterSymbol parameter)
    {
        SeparatedSyntaxList<ArgumentSyntax> arguments = call.ArgumentList.Arguments;

        foreach (ArgumentSyntax argument in arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == parameter.Name)
                return argument;
        }

        ArgumentSyntax positional = arguments[parameter.Ordinal];

        return positional.NameColon is null ? positional : null;
    }

    // The call's own leading and trailing trivia move onto the string, so only a comment between them is lost.
    private static bool HasCommentOutside(InvocationExpressionSyntax call, InterpolatedStringExpressionSyntax text)
    {
        return call.DescendantTrivia()
            .Any(trivia => call.Span.Contains(trivia.SpanStart) && !text.Span.Contains(trivia.SpanStart) && trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia);
    }

    private static bool IgnoresCulture(InterpolatedStringExpressionSyntax text, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (InterpolatedStringContentSyntax content in text.Contents)
        {
            if (content is InterpolationSyntax hole && !IgnoresCulture(hole, model, cancellationToken))
                return false;
        }

        return true;
    }

    // The culture reaches a value only when the value formats itself through it, and these types never do: a string
    // and a bool are not formattable, an enumeration is written by name, and a char and a Guid ignore the culture.
    private static bool IgnoresCulture(InterpolationSyntax hole, SemanticModel model, CancellationToken cancellationToken)
    {
        ITypeSymbol? type = model.GetTypeInfo(hole.Expression, cancellationToken).Type;

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[index: 0];

        return type is not null
            && (type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean or SpecialType.System_Char
                || type.TypeKind is TypeKind.Enum
                || type.ToDisplayString() is "System.Guid");
    }

    // The overload that formats an interpolated string with a provider and nothing else.
    private static bool IsFormattingCreate(IMethodSymbol method)
    {
        return method is { Name: "Create", ContainingType.SpecialType: SpecialType.System_String, Parameters.Length: 2 }
            && method.Parameters[index: 0].Type.Name is "IFormatProvider"
            && method.Parameters[index: 1].Type.Name is "DefaultInterpolatedStringHandler";
    }

    private static bool IsInvariantCulture(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
    {
        return model.GetSymbolInfo(expression, cancellationToken).Symbol is IPropertySymbol { Name: "InvariantCulture" } property
            && property.ContainingType.ToDisplayString() is "System.Globalization.CultureInfo";
    }

    private static bool IsNamedCreate(ExpressionSyntax expression)
    {
        return expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Create" };
    }
}
