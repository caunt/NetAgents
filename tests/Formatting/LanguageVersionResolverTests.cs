using Microsoft.CodeAnalysis.CSharp;

using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>Checks how the formatter resolves the language version the building SDK reports.</summary>
public sealed class LanguageVersionResolverTests
{
    private const string ProjectPath = "/Consumer.csproj";

    /// <summary>Never substitutes a version this package's own Roslyn reports as supported.</summary>
    [Fact]
    public void KeepsEveryDisplayedVersion()
    {
        foreach (LanguageVersion supported in Enum.GetValues<LanguageVersion>())
        {
            LanguageVersionResolver.LanguageVersionResolution resolution = LanguageVersionResolver.Resolve(supported.ToDisplayString(), ProjectPath);

            Assert.Equal(supported, resolution.Version);
            Assert.False(resolution.Substituted);
        }
    }

    /// <summary>Keeps the configured version, including aliases and surrounding whitespace.</summary>
    [Fact]
    public void KeepsSupportedVersion()
    {
        Assert.Equal(LanguageVersion.CSharp14, LanguageVersionResolver.Resolve(configured: "14.0", ProjectPath).Version);
        Assert.Equal(LanguageVersion.CSharp12, LanguageVersionResolver.Resolve(configured: "12", ProjectPath).Version);
        Assert.Equal(LanguageVersion.CSharp1, LanguageVersionResolver.Resolve(configured: "ISO-1", ProjectPath).Version);
        Assert.Equal(LanguageVersion.LatestMajor, LanguageVersionResolver.Resolve(configured: "latestMajor", ProjectPath).Version);
        Assert.Equal(LanguageVersion.CSharp14, LanguageVersionResolver.Resolve(configured: " 14.0 ", ProjectPath).Version);
        Assert.False(LanguageVersionResolver.Resolve(configured: "14.0", ProjectPath).Substituted);
    }

    /// <summary>Names the property, the value, and the project when the value is no language version.</summary>
    [Fact]
    public void RejectsUnknownVersion()
    {
        foreach (string configured in (string[])["banana", "csharp14", "13.0.1"])
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => ResolveWithoutResult(configured));

            Assert.Contains(expectedSubstring: "LangVersion", failure.Message, StringComparison.Ordinal);
            Assert.Contains(configured, failure.Message, StringComparison.Ordinal);
            Assert.Contains(ProjectPath, failure.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Resolves an unset value the way the compiler resolves an unset LangVersion.</summary>
    [Fact]
    public void ResolvesMissingVersionToDefault()
    {
        foreach (string configured in (string[])["", "   ", "\r\n "])
        {
            LanguageVersionResolver.LanguageVersionResolution resolution = LanguageVersionResolver.Resolve(configured, ProjectPath);

            Assert.Equal(LanguageVersion.Default, resolution.Version);
            Assert.False(resolution.Substituted);
        }
    }

    /// <summary>Formats with the newest available grammar when the SDK reports a newer version than this package knows.</summary>
    [Fact]
    public void SubstitutesPreviewForNewerVersion()
    {
        // The .NET 11 SDK fills LangVersion with 15.0, which this package's Roslyn cannot parse.
        foreach (string configured in (string[])["15.0", "15", "16.0", "99.9"])
        {
            LanguageVersionResolver.LanguageVersionResolution resolution = LanguageVersionResolver.Resolve(configured, ProjectPath);

            Assert.Equal(LanguageVersion.Preview, resolution.Version);
            Assert.True(resolution.Substituted);
        }
    }

    // Assert.Throws boxes returned values, so the resolution never leaves this method.
    private static void ResolveWithoutResult(string configured)
    {
        LanguageVersionResolver.LanguageVersionResolution resolution = LanguageVersionResolver.Resolve(configured, ProjectPath);

        Assert.Equal(LanguageVersion.Default, resolution.Version);
        Assert.False(resolution.Substituted);
    }
}
