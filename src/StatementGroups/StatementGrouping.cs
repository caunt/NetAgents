using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Formatting;

namespace NetAgents.Analyzers.StatementGroups;

/// <summary>
/// Splits statement lists into groups: runs of declarations, assignments, declarations or assignments of awaited
/// results, event subscriptions, calls, awaits, or argument checks, where a comment line opens a new group and any
/// other statement stands alone.
/// </summary>
/// <remarks>
/// Every block of statements is covered, whatever owns it: methods, constructors, operators, accessors, lambdas,
/// anonymous methods, local functions, nested blocks, switch sections, and top-level statements. The first
/// statement of a list is left alone, since NETAGENTS0023 owns the space after an opening brace.
/// </remarks>
internal static class StatementGrouping
{
    public const int Limit = 4;

    public const string RuleIdentifier = "NETAGENTS0039";
    public const string SizeRuleIdentifier = "NETAGENTS0040";

    public static ImmutableDictionary<TextSpan, TextChange> GetChanges(SyntaxNode root, SemanticModel model, SourceText source, CancellationToken cancellationToken)
    {
        ImmutableDictionary<TextSpan, TextChange>.Builder changes = ImmutableDictionary.CreateBuilder<TextSpan, TextChange>();
        StatementClassifier classifier = new(model, source, cancellationToken);

        foreach (List<StatementSyntax> statements in CollectStatementLists(root))
        {
            StatementKind previousKind = classifier.Classify(statements[index: 0]);

            for (int index = 1; index < statements.Count; index++)
            {
                StatementSyntax current = statements[index];
                StatementKind kind = classifier.Classify(current);
                bool separated = kind != previousKind || kind is StatementKind.Alone || StartsGroup(current);
                TextChange? change = GetChange(statements[index - 1], current, separated, source);

                if (change is TextChange groupChange)
                    changes.Add(current.GetFirstToken().Span, groupChange);

                previousKind = kind;
            }
        }

        return changes.ToImmutable();
    }

    public static List<StatementGroup> GetLargeGroups(SyntaxNode root, SemanticModel model, SourceText source, CancellationToken cancellationToken)
    {
        List<StatementGroup> groups = [];
        StatementClassifier classifier = new(model, source, cancellationToken);

        foreach (List<StatementSyntax> statements in CollectStatementLists(root))
        {
            int start = 0;
            StatementKind kind = classifier.Classify(statements[index: 0]);

            // The pass runs one step past the last statement, so the final group closes like any other.
            for (int index = 1; index <= statements.Count; index++)
            {
                StatementKind next = index < statements.Count ? classifier.Classify(statements[index]) : StatementKind.Alone;
                bool closes = index == statements.Count || next != kind || next is StatementKind.Alone || StartsGroup(statements[index]);

                if (!closes)
                    continue;

                if (index - start > Limit && IsLimited(statements, kind, start, index))
                    groups.Add(new StatementGroup(statements[start], statements[index - 1], kind, index - start));

                start = index;
                kind = next;
            }
        }

        return groups;
    }

    // A comment on a line of its own above the statement opens a new group; one sharing the statement's line
    // does not. A documentation comment always ends its own line.
    public static bool StartsGroup(StatementSyntax statement)
    {
        bool commented = false;

        foreach (SyntaxTrivia trivia in statement.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                return true;

            if (commented && trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                return true;

            bool comment = trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia);

            commented = comment || (commented && trivia.IsKind(SyntaxKind.WhitespaceTrivia));
        }

        return false;
    }

    private static void Add(List<List<StatementSyntax>> lists, SyntaxList<StatementSyntax> statements)
    {
        if (statements.Count < 2)
            return;

        lists.Add(item: [.. statements]);
    }

    private static List<List<StatementSyntax>> CollectStatementLists(SyntaxNode root)
    {
        List<List<StatementSyntax>> lists = [];
        List<StatementSyntax> topLevel = [];

        foreach (SyntaxNode node in root.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case BlockSyntax block:
                    {
                        Add(lists, block.Statements);

                        break;
                    }
                case SwitchSectionSyntax section:
                    {
                        Add(lists, section.Statements);

                        break;
                    }
                case GlobalStatementSyntax global:
                    {
                        topLevel.Add(global.Statement);

                        break;
                    }
                default:
                    break;
            }
        }

        if (topLevel.Count > 1)
            lists.Add(topLevel);

        return lists;
    }

    // A constructor that only stores its parameters, as in Price = price;, says what it does in its signature.
    private static bool CopiesParameter(StatementSyntax statement, ConstructorDeclarationSyntax constructor)
    {
        if (statement is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
            return false;

        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || assignment.Right is not IdentifierNameSyntax value)
            return false;

        foreach (ParameterSyntax parameter in constructor.ParameterList.Parameters)
        {
            if (string.Equals(parameter.Identifier.ValueText, value.Identifier.ValueText, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // The statements of a list may hold comment lines, blank lines, and nothing else between them. Only the
    // blank lines change: one goes directly above the first comment line, or above the statement when there is
    // no comment, and every other blank line goes.
    private static TextChange? GetChange(StatementSyntax previous, StatementSyntax current, bool separated, SourceText source)
    {
        int start = current.FullSpan.Start;
        TextLine first = source.Lines.GetLineFromPosition(start);
        TextLine statementLine = source.Lines.GetLineFromPosition(current.SpanStart);

        // NETAGENTS0016 adds the blank line that a statement it separates is missing; this rule leaves it alone.
        bool covered = StatementSpacing.RequiresSeparation(previous, source) || StatementSpacing.RequiresSeparation(current, source);

        // Statements sharing a line are split by the same edit NETAGENTS0016 makes.
        if (first.Start != start)
        {
            bool sharesLine = source.Lines.GetLineFromPosition(previous.Span.End).LineNumber == statementLine.LineNumber;

            return separated && sharesLine && !covered ? DeclarationSpacing.GetChange(previous, current, source) : null;
        }

        if (HasDirective(current))
            return null;

        List<TextLine> comments = [];
        int blankLines = 0;
        bool leadingBlank = false;

        for (int number = first.LineNumber; number < statementLine.LineNumber; number++)
        {
            TextLine line = source.Lines[number];

            if (!IsBlank(line, current, source))
            {
                comments.Add(line);

                continue;
            }

            leadingBlank |= number == first.LineNumber;
            blankLines++;
        }

        bool settled = separated ? blankLines == 1 && leadingBlank : blankLines == 0;

        if (settled || (separated && blankLines == 0 && covered))
            return null;

        // The blank line takes the line break the previous statement ends with.
        TextLine before = source.Lines[first.LineNumber - 1];
        List<string> replacement = separated ? [source.ToString(TextSpan.FromBounds(before.End, before.EndIncludingLineBreak))] : [];

        foreach (TextLine comment in comments)
            replacement.Add(source.ToString(comment.SpanIncludingLineBreak));

        return new TextChange(TextSpan.FromBounds(start, statementLine.Start), string.Concat(replacement));
    }

    private static bool HasDirective(StatementSyntax statement)
    {
        foreach (SyntaxTrivia trivia in statement.GetLeadingTrivia())
        {
            if (trivia.IsDirective || trivia.IsKind(SyntaxKind.DisabledTextTrivia) || trivia.IsKind(SyntaxKind.SkippedTokensTrivia))
                return true;
        }

        return false;
    }

    // A line inside a block comment is part of the comment, even when it is empty.
    private static bool IsBlank(TextLine line, StatementSyntax statement, SourceText source)
    {
        for (int position = line.Start; position < line.End; position++)
        {
            if (!char.IsWhiteSpace(source[position]))
                return false;
        }

        foreach (SyntaxTrivia trivia in statement.GetLeadingTrivia())
        {
            if (trivia.Span.Contains(line.Start) && !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                return false;
        }

        return true;
    }

    // Argument checks repeat the signature one parameter at a time, so a comment between them could only
    // restate it; the same holds for a constructor run that stores one parameter per line.
    private static bool IsLimited(List<StatementSyntax> statements, StatementKind kind, int start, int end)
    {
        if (kind is StatementKind.Alone or StatementKind.ArgumentCheck)
            return false;

        if (statements[start].Parent is not BlockSyntax { Parent: ConstructorDeclarationSyntax constructor })
            return true;

        for (int index = start; index < end; index++)
        {
            if (!CopiesParameter(statements[index], constructor))
                return true;
        }

        return false;
    }
}
