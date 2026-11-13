using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Documentation;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Documentation;

/// <summary>
/// Covers keywords written in documentation text, Boolean results worded as the .NET documentation words them,
/// and the fix that writes keywords as langword references.
/// </summary>
public sealed class LanguageKeywordTests
{
    /// <summary>
    /// Verifies langword references, keywords inside code, hyphenated prose, a Boolean result worded the .NET
    /// way, and members outside the public surface are accepted.
    /// </summary>
    /// <param name="members">The members declared inside a public class.</param>
    [Theory]
    [InlineData("/// <summary>Reads a name, or <see langword=\"null\"/> once none are left.</summary>\n/// <returns>The name read.</returns>\npublic string? Read() => null;")]
    [InlineData("/// <summary>Reads a name.</summary>\n/// <returns>A non-null name.</returns>\npublic string Read() => string.Empty;")]
    [InlineData("/// <summary>Reads a name.</summary>\n/// <example><code>if (Read() is null) { }</code></example>\npublic string? Read() => null;")]
    [InlineData("/// <summary>Checks the lamp.</summary>\n/// <returns><see langword=\"true\"/> if the lamp is lit; otherwise, <see langword=\"false\"/>.</returns>\npublic bool Check() => false;")]
    [InlineData("/// <summary>Gets a value indicating whether the lamp is lit.</summary>\n/// <value><see langword=\"true\"/> if <c>Lit</c> was called; otherwise, <see langword=\"false\"/>.</value>\npublic bool IsLit { get; }")]
    [InlineData("/// <summary>Reads a name, or null once none are left.</summary>\n/// <returns>The name read.</returns>\ninternal string? Read() => null;")]
    public async Task AcceptsLanguageWords(string members)
    {
        Assert.Empty(await Analyze(members));
    }

    /// <summary>
    /// Verifies a Boolean result or value worded another way is reported, without a fix, since only its author
    /// knows the condition it states.
    /// </summary>
    /// <param name="members">The members declared inside a public class.</param>
    [Theory]
    [InlineData("/// <summary>Checks the lamp.</summary>\n/// <returns>Whether the lamp is lit.</returns>\npublic bool Check() => false;")]
    [InlineData("/// <summary>Gets a value indicating whether the lamp is lit.</summary>\n/// <value>The lamp's state.</value>\npublic bool IsLit { get; }")]
    public async Task ReportsBooleanWording(string members)
    {
        Diagnostic diagnostic = Assert.Single(await Analyze(members));

        Assert.Contains(expectedSubstring: "otherwise", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(diagnostic.Properties);
    }

    /// <summary>
    /// Verifies the fix writes keywords in text and keywords wrapped in a code element as langword references,
    /// which also settles a Boolean result written with plain keywords.
    /// </summary>
    /// <param name="members">The members as written.</param>
    /// <param name="expected">The members after the fix.</param>
    [Theory]
    [InlineData("/// <summary>Reads a name, or null once none are left.</summary>\n/// <returns>The name read.</returns>\npublic string? Read() => null;",
        "/// <summary>Reads a name, or <see langword=\"null\"/> once none are left.</summary>\n/// <returns>The name read.</returns>\npublic string? Read() => null;")]
    [InlineData("/// <summary>Reads a name, or <c>null</c> once none are left.</summary>\n/// <returns>The name read.</returns>\npublic string? Read() => null;",
        "/// <summary>Reads a name, or <see langword=\"null\"/> once none are left.</summary>\n/// <returns>The name read.</returns>\npublic string? Read() => null;")]
    [InlineData("/// <summary>Checks the lamp.</summary>\n/// <returns>true if the lamp is lit; otherwise, false.</returns>\npublic bool Check() => false;",
        "/// <summary>Checks the lamp.</summary>\n/// <returns><see langword=\"true\"/> if the lamp is lit; otherwise, <see langword=\"false\"/>.</returns>\npublic bool Check() => false;")]
    public async Task WritesLanguageWords(string members, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixLanguageKeywords(Wrap(members), fixAll: true));
    }

    private static async Task<Diagnostic[]> Analyze(string members)
    {
        return await AnalyzerTestHarness
            .Analyze(new LanguageKeywordAnalyzer(), Wrap(members))
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    private static string Wrap(string members)
    {
        return $$"""
            /// <summary>Holds the members under test.</summary>
            public class ExampleType
            {
            {{members}}
            }
            """;
    }
}
