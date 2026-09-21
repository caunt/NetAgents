using System.Collections.Immutable;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

using NetAgents.Analyzers.CodeFixes.Ordering;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>Checks the analyzers the formatter hosts when the compiler's own loader refuses them.</summary>
public sealed class AnalyzerCatalogTests
{
    private static readonly string CodeStylePath = Path.Combine(
        typeof(AnalyzerCatalogTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static attribute => string.Equals(attribute.Key, b: "CodeStyleDirectory", StringComparison.Ordinal)).Value
            ?? throw new InvalidOperationException(message: "The building SDK's code-style directory is unknown."),
        path2: "Microsoft.CodeAnalysis.CSharp.CodeStyle.dll"
    );

    /// <summary>Constructs the same analyzers the compiler's loader does, without its version gate.</summary>
    [Fact]
    public void HostsTheAnalyzersTheCompilerLoaderReturns()
    {
        using AnalyzerLoader loader = new();

        AnalyzerFileReference reference = new(CodeStylePath, loader);
        string[] loaded = GetIdentifiers([.. reference.GetAnalyzers(LanguageNames.CSharp)]);
        (bool read, ImmutableArray<DiagnosticAnalyzer> hosted, string[] unavailable) = AnalyzerCatalog.Host(CodeStylePath, loader);

        Assert.True(read);
        Assert.Empty(unavailable);
        Assert.False(hosted.IsEmpty);

        // An SDK built against a newer Roslyn than this package leaves the compiler's loader with nothing,
        // which is the case the hosted set exists for; wherever it does load, both sets must agree.
        if (loaded.Length > 0)
            Assert.Equal(loaded, GetIdentifiers([.. hosted]));
    }

    /// <summary>Leaves an assembly that declares no analyzer at all unreported.</summary>
    [Fact]
    public void IgnoresAssembliesThatDeclareNoAnalyzer()
    {
        using AnalyzerLoader loader = new();

        string path = typeof(MemberOrderingCodeFixProvider).Assembly.Location;
        (AnalyzerReference[] references, string[] hosted, string[] unavailable) = AnalyzerCatalog.Load([path], loader);

        Assert.Empty(hosted);
        Assert.Empty(unavailable);
        Assert.Empty(references.Single().GetAnalyzers(LanguageNames.CSharp).ToArray());
    }

    /// <summary>Keeps the SDK's code-style rules available to the formatter on any building SDK.</summary>
    [Fact]
    public void LoadsTheCodeStyleAnalyzersTheSdkSupplies()
    {
        using AnalyzerLoader loader = new();

        (AnalyzerReference[] references, string[] hosted, string[] unavailable) = AnalyzerCatalog.Load([CodeStylePath], loader);
        string[] identifiers = GetIdentifiers([.. references.SelectMany(static reference => reference.GetAnalyzers(LanguageNames.CSharp).ToArray())]);

        Assert.Empty(unavailable);
        Assert.Contains(expected: "IDE0005", identifiers, StringComparer.Ordinal);
        Assert.Contains(expected: "IDE0008", identifiers, StringComparer.Ordinal);
        Assert.Contains(expected: "IDE0055", identifiers, StringComparer.Ordinal);

        // The announcement exists only where the compiler's loader refused this assembly outright.
        Assert.True(hosted.Length is 0 or 1);
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

            (AnalyzerReference[] references, string[] hosted, string[] unavailable) = AnalyzerCatalog.Load([path], loader);

            Assert.Empty(hosted);
            Assert.Contains(expectedSubstring: "Broken.Analyzers.dll", Assert.Single(unavailable), StringComparison.Ordinal);
            Assert.Empty(references.Single().GetAnalyzers(LanguageNames.CSharp).ToArray());
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    private static string[] GetIdentifiers(DiagnosticAnalyzer[] analyzers)
    {
        return [.. analyzers.SelectMany(static analyzer => analyzer.SupportedDiagnostics.Select(static descriptor => descriptor.Id))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }
}
