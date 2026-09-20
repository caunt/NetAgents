using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Readability;

internal static class ExplicitLiteralArgumentName
{
    public const string RuleIdentifier = "NETAGENTS0009";

    public static ImmutableDictionary<TextSpan, ArgumentNaming> GetChanges(SyntaxNode root, SemanticModel model, CancellationToken cancellationToken)
    {
        ImmutableDictionary<TextSpan, ArgumentNaming>.Builder changes = ImmutableDictionary.CreateBuilder<TextSpan, ArgumentNaming>();

        foreach (BaseArgumentListSyntax list in root.DescendantNodes().OfType<BaseArgumentListSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNaming? naming = CreateNaming(list, model, cancellationToken);

            if (naming is null)
                continue;

            foreach (TextSpan span in naming.Spans)
                changes[span] = naming;
        }

        return changes.ToImmutable();
    }

    public static bool IsViolation(ArgumentSyntax argument, SemanticModel model, CancellationToken cancellationToken)
    {
        return RequiresName(argument, model, cancellationToken) || AllowsRemovingName(argument, model, cancellationToken);
    }

    private static bool AllowsRemovingName(ArgumentSyntax argument, SemanticModel model, CancellationToken cancellationToken)
    {
        return argument.NameColon is not null && Unwrap(argument.Expression) is not LiteralExpressionSyntax and not DefaultExpressionSyntax && argument.Parent is BaseArgumentListSyntax list && IsPositional(list, model, cancellationToken);
    }

    private static bool AppendExpanded(
        BaseArgumentListSyntax list,
        SemanticModel model,
        ImmutableArray<IParameterSymbol> parameters,
        int expanded,
        List<ArgumentSyntax> rewritten,
        ImmutableArray<TextSpan>.Builder spans,
        CancellationToken cancellationToken
    )
    {
        SeparatedSyntaxList<ArgumentSyntax> arguments = list.Arguments;
        List<TextSpan> named = [];

        for (int index = expanded; index < arguments.Count; index++)
        {
            if (RequiresName(arguments[index], model, cancellationToken))
                named.Add(arguments[index].Span);
        }

        if (named.Count == 0)
        {
            for (int index = expanded; index < arguments.Count; index++)
                rewritten.Add(arguments[index]);

            return true;
        }

        ArgumentSyntax? collection = CreateCollection(list, model, parameters[parameters.Length - 1], expanded, cancellationToken);

        if (collection is null)
            return false;

        rewritten.Add(collection);
        spans.AddRange(named);

        return true;
    }

    private static ArgumentSyntax? CreateCollection(BaseArgumentListSyntax list, SemanticModel model, IParameterSymbol parameter, int expanded, CancellationToken cancellationToken)
    {
        NameColonSyntax? name = CreateName(parameter);

        if (name is null || !SupportsCollections(list) || IsInsideExpressionTree(list, model, cancellationToken))
            return null;

        SeparatedSyntaxList<ArgumentSyntax> arguments = list.Arguments;
        ArgumentSyntax first = arguments[expanded];
        ArgumentSyntax last = arguments[arguments.Count - 1];

        // Collapsing drops the separators between the expanded items, so a comment there would be lost.
        if (HasComments(list, TextSpan.FromBounds(first.SpanStart, last.Span.End)))
            return null;

        List<CollectionElementSyntax> elements = [];

        for (int index = expanded; index < arguments.Count; index++)
        {
            if (!arguments[index].RefKindKeyword.IsKind(SyntaxKind.None))
                return null;

            elements.Add(SyntaxFactory.ExpressionElement(arguments[index].Expression.WithoutTrivia()));
        }

        SyntaxToken comma = CreateToken(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space);

        SeparatedSyntaxList<CollectionElementSyntax> items = SyntaxFactory.SeparatedList(elements, Enumerable.Repeat(comma, elements.Count - 1));

        CollectionExpressionSyntax collection = SyntaxFactory.CollectionExpression(CreateToken(SyntaxKind.OpenBracketToken), items, CreateToken(SyntaxKind.CloseBracketToken));

        return SyntaxFactory.Argument(collection)
            .WithNameColon(name)
            .WithLeadingTrivia(first.GetLeadingTrivia())
            .WithTrailingTrivia(last.GetTrailingTrivia());
    }

    private static NameColonSyntax? CreateName(IParameterSymbol parameter)
    {
        string name = parameter.Name;

        if (name.Length == 0 || parameter.IsThis || !SyntaxFacts.IsValidIdentifier(name))
            return null;

        SyntaxToken identifier = SyntaxFacts.GetKeywordKind(name) is SyntaxKind.None
            ? SyntaxFactory.Identifier(SyntaxTriviaList.Empty, name, SyntaxTriviaList.Empty)
            : SyntaxFactory.Identifier(SyntaxTriviaList.Empty, SyntaxKind.IdentifierToken, "@" + name, name, SyntaxTriviaList.Empty);

        return SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(identifier), CreateToken(SyntaxKind.ColonToken).WithTrailingTrivia(SyntaxFactory.Space));
    }

    private static ArgumentNaming? CreateNaming(BaseArgumentListSyntax list, SemanticModel model, CancellationToken cancellationToken)
    {
        if (!IsCallable(list, model, cancellationToken, out ISymbol? target, out ImmutableArray<IParameterSymbol> parameters))
            return null;

        SeparatedSyntaxList<ArgumentSyntax> arguments = list.Arguments;
        int expanded = GetExpandedStart(list, model, parameters, cancellationToken);
        int simple = expanded < 0 ? arguments.Count : expanded;
        List<ArgumentSyntax> rewritten = [];
        ImmutableArray<TextSpan>.Builder spans = ImmutableArray.CreateBuilder<TextSpan>();

        for (int index = 0; index < simple; index++)
        {
            ArgumentSyntax argument = arguments[index];

            if (RequiresName(argument, model, cancellationToken))
            {
                // The analyzer resolves the parameter the same way, so everything it reports stays fixable.
                IParameterSymbol? parameter = ResolveParameter(argument, model, cancellationToken);
                NameColonSyntax? name = parameter is null ? null : CreateName(parameter);

                if (name is null)
                    return null;

                rewritten.Add(WithName(argument, name));
                spans.Add(argument.Span);
            }
            else if (AllowsRemovingName(argument, model, cancellationToken))
            {
                rewritten.Add(WithoutName(argument));
                spans.Add(argument.Span);
            }
            else
            {
                rewritten.Add(argument);
            }
        }

        if (expanded >= 0 && !AppendExpanded(list, model, parameters, expanded, rewritten, spans, cancellationToken))
            return null;

        if (spans.Count == 0 || !IsOrderLegal(list, rewritten))
            return null;

        BaseArgumentListSyntax? replacement = Replace(list, rewritten);

        return replacement is not null && KeepsTarget(list, replacement, target, model, cancellationToken)
            ? new ArgumentNaming(list, replacement, spans.ToImmutable())
            : null;
    }

    private static SyntaxToken CreateToken(SyntaxKind kind)
    {
        // The plain token factories carry elastic markers, which make code actions reformat the region.
        return SyntaxFactory.Token(SyntaxTriviaList.Empty, kind, SyntaxTriviaList.Empty);
    }

    private static SyntaxNode? FindAnchor(SyntaxNode node)
    {
        SyntaxNode? initializer = null;

        foreach (SyntaxNode ancestor in node.Ancestors())
        {
            // A local declaration also offers its enclosing statement, which speculates more reliably.
            if (ancestor is EqualsValueClauseSyntax)
            {
                initializer ??= ancestor;

                continue;
            }

            if (ancestor is StatementSyntax or ArrowExpressionClauseSyntax or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax)
                return ancestor;
        }

        return initializer;
    }

    private static int GetExpandedStart(
        BaseArgumentListSyntax list,
        SemanticModel model,
        ImmutableArray<IParameterSymbol> parameters,
        CancellationToken cancellationToken
    )
    {
        int position = parameters.Length - 1;

        if (!parameters[position].IsParams || list.Arguments.Count <= position)
            return -1;

        // Expanded arguments carry no argument operation of their own; the normal form keeps one.
        return model.GetOperation(list.Arguments[position], cancellationToken) is IArgumentOperation ? -1 : position;
    }

    private static ImmutableArray<IParameterSymbol> GetParameters(ISymbol? symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol property => property.Parameters,
            _ => [],
        };
    }

    private static bool HasComments(BaseArgumentListSyntax list, TextSpan range)
    {
        return list.DescendantTrivia().Any(
            trivia =>
            range.Contains(trivia.SpanStart)
                && trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
        );
    }

    private static bool IsCallable(
        BaseArgumentListSyntax list,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ISymbol? target,
        out ImmutableArray<IParameterSymbol> parameters
    )
    {
        target = null;
        parameters = [];

        if (list.Parent is null || list.ContainsDirectives || list.ContainsDiagnostics)
            return false;

        SymbolInfo resolution = model.GetSymbolInfo(list.Parent, cancellationToken);

        if (resolution.CandidateReason is not CandidateReason.None || resolution.Symbol is null)
            return false;

        if (resolution.Symbol is IMethodSymbol { IsVararg: true } or IMethodSymbol { MethodKind: MethodKind.FunctionPointerSignature })
            return false;

        target = resolution.Symbol;
        parameters = GetParameters(resolution.Symbol);

        return !parameters.IsEmpty;
    }

    private static bool IsInsideExpressionTree(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (AnonymousFunctionExpressionSyntax ancestor in node.Ancestors().OfType<AnonymousFunctionExpressionSyntax>())
        {
            ITypeSymbol? converted = model.GetTypeInfo(ancestor, cancellationToken).ConvertedType;

            if (converted?.ContainingNamespace?.ToDisplayString() is "System.Linq.Expressions")
                return true;
        }

        return false;
    }

    private static bool IsOrderLegal(BaseArgumentListSyntax list, List<ArgumentSyntax> rewritten)
    {
        bool trailing = true;

        for (int index = rewritten.Count - 1; index >= 0; index--)
        {
            if (rewritten[index].NameColon is null)
                trailing = false;
            else if (!trailing && !SupportsLeadingNames(list))
                return false;
        }

        return true;
    }

    private static bool IsPositional(BaseArgumentListSyntax list, SemanticModel model, CancellationToken cancellationToken)
    {
        if (!IsCallable(list, model, cancellationToken, out ISymbol? target, out ImmutableArray<IParameterSymbol> parameters))
            return false;

        SeparatedSyntaxList<ArgumentSyntax> arguments = list.Arguments;

        for (int index = 0; index < arguments.Count; index++)
        {
            if (model.GetOperation(arguments[index], cancellationToken) is not IArgumentOperation operation)
                return false;

            // Positional binding only reproduces this mapping when every argument already sits in its own slot.
            if (operation.Parameter is not IParameterSymbol parameter || parameter.Ordinal != index || parameter.IsParams)
                return false;
        }

        return target is not null && parameters.Length >= arguments.Count;
    }

    private static bool KeepsTarget(
        BaseArgumentListSyntax list,
        BaseArgumentListSyntax replacement,
        ISymbol? original,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        SyntaxNode? anchor = FindAnchor(list);

        if (anchor is null || original is null)
            return false;

        SyntaxAnnotation marker = new();
        SyntaxNode candidate = anchor.ReplaceNode(list, replacement.WithAdditionalAnnotations(marker));
        SemanticModel? speculative = null;
        int position = anchor.SpanStart;

        bool acquired = candidate switch
        {
            StatementSyntax statement => model.TryGetSpeculativeSemanticModel(position, statement, out speculative),
            EqualsValueClauseSyntax clause => model.TryGetSpeculativeSemanticModel(position, clause, out speculative),
            ArrowExpressionClauseSyntax clause => model.TryGetSpeculativeSemanticModel(position, clause, out speculative),
            ConstructorInitializerSyntax initializer => model.TryGetSpeculativeSemanticModel(position, initializer, out speculative),
            PrimaryConstructorBaseTypeSyntax baseType => model.TryGetSpeculativeSemanticModel(position, baseType, out speculative),
            _ => false,
        };

        if (!acquired || speculative is null)
            return false;

        SyntaxNode? rebound = candidate.GetAnnotatedNodes(marker).FirstOrDefault()?.Parent;

        if (rebound is null)
            return false;

        SymbolInfo rebinding = speculative.GetSymbolInfo(rebound, cancellationToken);

        return rebinding.CandidateReason is CandidateReason.None && SymbolEqualityComparer.Default.Equals(rebinding.Symbol, original);
    }

    private static BaseArgumentListSyntax? Replace(BaseArgumentListSyntax list, List<ArgumentSyntax> rewritten)
    {
        SeparatedSyntaxList<ArgumentSyntax> updated = SyntaxFactory.SeparatedList(rewritten, list.Arguments.GetSeparators().Take(rewritten.Count - 1));

        return list switch
        {
            ArgumentListSyntax plain => plain.WithArguments(updated),
            BracketedArgumentListSyntax bracketed => bracketed.WithArguments(updated),
            _ => null,
        };
    }

    private static bool RequiresName(ArgumentSyntax argument, SemanticModel model, CancellationToken cancellationToken)
    {
        return argument.NameColon is null && Unwrap(argument.Expression) is LiteralExpressionSyntax or DefaultExpressionSyntax && ResolveParameter(argument, model, cancellationToken) is not null;
    }

    private static IParameterSymbol? ResolveParameter(ArgumentSyntax argument, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetOperation(argument, cancellationToken) is IArgumentOperation operation)
            return operation.Parameter;

        if (argument.Parent is not BaseArgumentListSyntax argumentList || argumentList.Parent is null)
            return null;

        ISymbol? invokedSymbol = model.GetSymbolInfo(argumentList.Parent, cancellationToken).Symbol;
        ImmutableArray<IParameterSymbol> parameters = GetParameters(invokedSymbol);
        int argumentIndex = argumentList.Arguments.IndexOf(argument);

        return argumentIndex < parameters.Length
            ? parameters[argumentIndex]
            : parameters.Length > 0 && parameters[parameters.Length - 1].IsParams
            ? parameters[parameters.Length - 1]
            : null;
    }

    private static bool SupportsCollections(SyntaxNode node)
    {
        return node.SyntaxTree.Options is CSharpParseOptions options && options.LanguageVersion >= LanguageVersion.CSharp12;
    }

    private static bool SupportsLeadingNames(SyntaxNode node)
    {
        return node.SyntaxTree.Options is CSharpParseOptions options && options.LanguageVersion >= LanguageVersion.CSharp7_2;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        // Signs and parentheses nest in either order, so unwrap until neither remains.
        while (expression is ParenthesizedExpressionSyntax or PrefixUnaryExpressionSyntax)
        {
            expression = expression is ParenthesizedExpressionSyntax parenthesizedExpression
                ? parenthesizedExpression.Expression
                : ((PrefixUnaryExpressionSyntax)expression).Operand;
        }

        return expression;
    }

    private static ArgumentSyntax WithName(ArgumentSyntax argument, NameColonSyntax name)
    {
        // The name becomes the first token, so the argument's own leading trivia has to move onto it.
        SyntaxTriviaList leading = argument.GetLeadingTrivia();

        return argument.WithoutLeadingTrivia().WithNameColon(name).WithLeadingTrivia(leading);
    }

    private static ArgumentSyntax WithoutName(ArgumentSyntax argument)
    {
        SyntaxTriviaList leading = argument.GetLeadingTrivia();

        return argument.WithNameColon(nameColon: null).WithLeadingTrivia(leading);
    }

    internal sealed class ArgumentNaming(BaseArgumentListSyntax list, BaseArgumentListSyntax replacement, ImmutableArray<TextSpan> spans)
    {
        public BaseArgumentListSyntax List { get; } = list;

        public BaseArgumentListSyntax Replacement { get; } = replacement;

        public ImmutableArray<TextSpan> Spans { get; } = spans;
    }
}
