using System;
using System.Collections.Generic;

namespace NetAgents.Analyzers.Documentation;

internal static class DocumentationInformation
{
    public const string RuleIdentifier = "NETAGENTS0028";

    // Generic English and documentation boilerplate only. A shipped rule cannot know which domain
    // words one codebase uses as filler, so anything outside this list counts as something said.
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "be", "by", "for", "from", "in", "is", "its", "of", "on",
        "or", "that", "the", "this", "to", "with",
        "contain", "contains", "create", "creates", "define", "defines", "describe", "describes",
        "get", "gets", "initialize", "initializes", "provide", "provides", "represent", "represents",
        "return", "returns", "set", "sets", "specifies", "specify", "use", "used", "uses",
        "contract", "field", "instance", "method", "new", "object", "operation", "property",
        "value", "values",
    };

    // Reports whether the documented name already carries everything the text says. Words are
    // compared rather than phrases, so a summary that only rearranges the name describes nothing.
    public static bool RestatesName(string text, string name)
    {
        HashSet<string> said = new(Describe(text), StringComparer.OrdinalIgnoreCase);

        if (said.Count == 0)
            return true;

        said.ExceptWith(Describe(name));

        return said.Count == 0;
    }

    // Identifiers reach here from both sides, so camel humps split the same way in prose as in a
    // name, and a word the filler list covers is dropped wherever it appears.
    private static List<string> Describe(string text)
    {
        string stripped = StripTags(text);
        List<string> words = [];
        int start = -1;

        for (int index = 0; index <= stripped.Length; index++)
        {
            bool letter = index < stripped.Length && char.IsLetterOrDigit(stripped[index]);

            if (start >= 0 && (!letter || StartsWord(stripped, index)))
            {
                Take(words, stripped, start, index - start);
                start = -1;
            }

            if (letter && start < 0)
                start = index;
        }

        return words;
    }

    // A word starts at an upper-case letter following a lower-case one, and also at the last
    // upper-case letter of a run, which is where an acronym hands over: TElement, XmlReader, ID.
    private static bool StartsWord(string text, int index)
    {
        return index != 0 && char.IsUpper(text[index]) && (!char.IsUpper(text[index - 1]) || (index + 1 < text.Length && char.IsLower(text[index + 1])));
    }

    // Tags carry no prose, and a name written inside a cref or a code element must not count as
    // having described anything, which is exactly how the emptiest summaries are written.
    private static string StripTags(string text)
    {
        List<char> stripped = [];
        int depth = 0;

        foreach (char character in text)
        {
            if (character == '<')
                depth++;
            else if (character == '>')
                depth = Math.Max(val1: 0, depth - 1);
            else if (depth == 0)
                stripped.Add(character);
        }

        return new([.. stripped]);
    }

    private static void Take(List<string> words, string text, int start, int length)
    {
        string candidate = text.Substring(start, length);

        if (!Filler.Contains(candidate))
            words.Add(candidate);
    }
}
