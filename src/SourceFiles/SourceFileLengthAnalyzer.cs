using System;
using System.Collections.Generic;
using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.SourceFiles;

/// <summary>
/// Limits authored source files to fewer than one thousand lines, not counting lines that hold only comments.
/// </summary>
/// <remarks>
/// A line is left out when everything on it but white space belongs to comments of any kind: <c>//</c> line
/// comments, <c>/* */</c> block comments, <c>///</c> and <c>/** */</c> XML documentation comments. Every line
/// of a block comment is left out, blank ones inside it included. A line still counts when it holds code next
/// to a comment, as in <c>int total = 0; // running sum</c>, and blank lines between members count as well.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SourceFileLengthAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0010";

    private const int Limit = 1_000;

    private static readonly DiagnosticDescriptor Rule = new(RuleIdentifier,
        title: "Source files must contain fewer than one thousand lines besides comments",
        messageFormat: "Split this file by responsibility; it has {0} lines besides comments, and authored files may have at most 999",
        category: "Structure",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        SourceText text = context.Tree.GetText(context.CancellationToken);
        TextLineCollection lines = text.Lines;

        // The required final newline closes the last authored line instead of starting another one.
        int authoredLines = lines.Count > 0 && lines[lines.Count - 1].Span.IsEmpty ? lines.Count - 1 : lines.Count;

        // Finding comments walks the whole tree, so a file under the limit even with its comments stops here.
        if (authoredLines < Limit)
            return;

        List<TextSpan> comments = CollectComments(context.Tree.GetRoot(context.CancellationToken));
        int countedLines = authoredLines - CountCommentLines(text, comments, authoredLines);

        if (countedLines < Limit)
            return;

        Location location = Location.Create(context.Tree, new TextSpan(start: 0, length: 0));
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, countedLines.ToString(CultureInfo.InvariantCulture)));
    }

    private static List<TextSpan> CollectComments(SyntaxNode root)
    {
        List<TextSpan> comments = [];

        // Trivia arrives in document order; documentation comments come as one structured trivia each. Their span
        // starts after the opening /// or /**, which belongs to the first token inside, so the full span is taken.
        foreach (SyntaxTrivia trivia in root.DescendantTrivia())
        {
            if (IsComment(trivia))
                comments.Add(trivia.FullSpan);
        }

        return comments;
    }

    private static int CountCommentLines(SourceText text, List<TextSpan> comments, int authoredLines)
    {
        int commentLines = 0;

        // Lines and comments both run in document order, so one cursor walks the comments alongside the lines.
        int cursor = 0;

        for (int index = 0; index < authoredLines; index++)
        {
            if (HoldsOnlyComments(text, text.Lines[index].Span, comments, ref cursor))
                commentLines++;
        }

        return commentLines;
    }

    private static bool HoldsOnlyComments(SourceText text, TextSpan line, List<TextSpan> comments, ref int cursor)
    {
        int position = SkipWhiteSpace(text, line.Start, line.End);
        bool commented = false;

        // Each pass steps over one comment and the white space after it; anything else left on the line is code.
        while (position < line.End && IsCovered(comments, ref cursor, position))
        {
            position = SkipWhiteSpace(text, Math.Min(comments[cursor].End, line.End), line.End);
            commented = true;
        }

        // A line with nothing but white space is a comment line only inside a block comment that runs through it.
        return position == line.End && (commented || IsCovered(comments, ref cursor, position));
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
            or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia;
    }

    private static bool IsCovered(List<TextSpan> comments, ref int cursor, int position)
    {
        while (cursor < comments.Count && comments[cursor].End <= position)
            cursor++;

        return cursor < comments.Count && comments[cursor].Start <= position;
    }

    private static int SkipWhiteSpace(SourceText text, int position, int end)
    {
        int current = position;

        while (current < end && char.IsWhiteSpace(text[current]))
            current++;

        return current;
    }
}
