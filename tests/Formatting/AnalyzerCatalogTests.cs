using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.CodeFixes.Ordering;
using NetAgents.Analyzers.Reliability;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>Checks the analyzers the formatter hosts when the compiler's own loader refuses them.</summary>
public sealed class AnalyzerCatalogTests
{
    private static readonly string CodeStylePath = Path.Combine(typeof(AnalyzerCatalogTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(static attribute => attribute.Key is "CodeStyleDirectory")
        .Value
            ?? throw new InvalidOperationException(message: "The building SDK's code-style directory is unknown."),
        path2: "Microsoft.CodeAnalysis.CSharp.CodeStyle.dll");

    /// <summary>Hosts a suppressor, which describes suppressions where an analyzer describes rules.</summary>
    [Fact]
    public void HostsSuppressorsThatReportNoDiagnosticsOfTheirOwn()
    {
        using AnalyzerLoader loader = new();

        string path = typeof(ExhaustiveSwitchSuppressor).Assembly.Location;
        AnalyzerCatalog.AnalyzerHostingResult hosting = AnalyzerCatalog.Host(path, loader);

        Assert.True(hosting.Read);
        Assert.Empty(hosting.Unavailable);

        // The hosted copy comes from a separate load context, so only the name identifies it.
        Assert.Contains(collection: [.. hosting.Analyzers], static analyzer => analyzer.GetType().FullName == typeof(ExhaustiveSwitchSuppressor).FullName);
    }

    /// <summary>Constructs the same analyzers the compiler's loader does, without its version gate.</summary>
    [Fact]
    public void HostsTheAnalyzersTheCompilerLoaderReturns()
    {
        using AnalyzerLoader loader = new();

        AnalyzerFileReference reference = new(CodeStylePath, loader);
        string[] loaded = GetIdentifiers(analyzers: [.. reference.GetAnalyzers(LanguageNames.CSharp)]);
        AnalyzerCatalog.AnalyzerHostingResult hosting = AnalyzerCatalog.Host(CodeStylePath, loader);

        Assert.True(hosting.Read);
        Assert.Empty(hosting.Unavailable);
        Assert.False(hosting.Analyzers.IsEmpty);

        // An SDK built against a newer Roslyn than this package leaves the compiler's loader with nothing,
        // which is the case the hosted set exists for; wherever it does load, both sets must agree.
        if (loaded.Length > 0)
            Assert.Equal(loaded, GetIdentifiers(analyzers: [.. hosting.Analyzers]));
    }

    /// <summary>Leaves an assembly that declares no analyzer at all unreported.</summary>
    [Fact]
    public void IgnoresAssembliesThatDeclareNoAnalyzer()
    {
        using AnalyzerLoader loader = new();

        string path = typeof(MemberOrderingCodeFixProvider).Assembly.Location;
        AnalyzerCatalog.AnalyzerLoadingResult loading = AnalyzerCatalog.Load(analyzerPaths: [path], loader);

        Assert.Empty(loading.Hosted);
        Assert.Empty(loading.Unavailable);
        Assert.Empty(loading.References.Single().GetAnalyzers(LanguageNames.CSharp).ToArray());
    }

    /// <summary>Keeps the SDK's code-style rules available to the formatter on any building SDK.</summary>
    [Fact]
    public void LoadsTheCodeStyleAnalyzersTheSdkSupplies()
    {
        using AnalyzerLoader loader = new();

        AnalyzerCatalog.AnalyzerLoadingResult loading = AnalyzerCatalog.Load(analyzerPaths: [CodeStylePath], loader);
        string[] identifiers = GetIdentifiers(analyzers: [.. loading.References.SelectMany(static reference => reference.GetAnalyzers(LanguageNames.CSharp).ToArray())]);

        Assert.Empty(loading.Unavailable);
        Assert.Contains(expected: "IDE0005", identifiers, StringComparer.Ordinal);
        Assert.Contains(expected: "IDE0008", identifiers, StringComparer.Ordinal);
        Assert.Contains(expected: "IDE0055", identifiers, StringComparer.Ordinal);

        // The announcement exists only where the compiler's loader refused this assembly outright.
        Assert.True(loading.Hosted.Length is 0 or 1);
    }

    /// <summary>Names the file when neither loader can read the analyzers it was configured with.</summary>
    [Fact]
    public void ReportsFilesThatHoldNoLoadableAssembly()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory(prefix: "netagents-catalog-");

        try
        {
            string path = Path.Combine(workspace.FullName, path2: "Broken.Analyzers.dll");

            File.WriteAllText(path, contents: "This file is not a managed assembly.\n");

            using AnalyzerLoader loader = new();

            AnalyzerCatalog.AnalyzerLoadingResult loading = AnalyzerCatalog.Load(analyzerPaths: [path], loader);

            Assert.Empty(loading.Hosted);
            Assert.Contains(expectedSubstring: "Broken.Analyzers.dll", Assert.Single(loading.Unavailable), StringComparison.Ordinal);
            Assert.Empty(loading.References.Single().GetAnalyzers(LanguageNames.CSharp).ToArray());
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    private static string[] GetIdentifiers(DiagnosticAnalyzer[] analyzers)
    {
        return [.. analyzers.SelectMany(static analyzer => analyzer.SupportedDiagnostics.Select(static descriptor => descriptor.Id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }
}
