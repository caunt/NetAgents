using Microsoft.CodeAnalysis.CSharp;

namespace NetAgents.BuildTasks;

internal static class LanguageVersionResolver
{
    // The building SDK fills LangVersion with the newest version it supports, so a consumer on a newer
    // SDK than this package's Roslyn asks for a version this parser has never heard of (net11.0 sends
    // 15.0). Formatting with the newest grammar available beats failing the build, and an unset value
    // resolves the way the compiler resolves it.
    public static (LanguageVersion Version, bool Substituted) Resolve(string configured, string projectPath)
    {
        string requested = configured.Trim();

        if (requested.Length == 0)
            return (LanguageVersion.Default, false);

        if (LanguageVersionFacts.TryParse(requested, out LanguageVersion parsed))
            return (parsed, false);

        // A value that is no language version at all stays a failure, named property and all; the
        // compiler rejects it as CS1617 immediately afterwards.
        return IsNewerThanSupported(requested)
            ? (LanguageVersion.Preview, true)
            : throw new InvalidOperationException($"The LangVersion value '{requested}' in {projectPath} is not a C# language version.");
    }

    private static bool IsNewerThanSupported(string requested)
    {
        // Version requires both components, while LangVersion also accepts a bare major such as 15.
        string candidate = requested.Contains(value: '.', StringComparison.Ordinal) ? requested : requested + ".0";

        if (!Version.TryParse(candidate, out Version? version))
            return false;

        LanguageVersion supported = LanguageVersion.LatestMajor.MapSpecifiedToEffectiveVersion();

        return version > Version.Parse(supported.ToDisplayString());
    }
}
