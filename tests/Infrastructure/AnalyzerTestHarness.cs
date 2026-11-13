using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.Analyzers.Tests.Infrastructure;

internal static class AnalyzerTestHarness
{
    internal static readonly MetadataReference[] References = [.. Directory
        .EnumerateFiles(Path.GetDirectoryName(typeof(string).Assembly.Location)
            ?? throw new InvalidOperationException(message: "The runtime assembly directory is unavailable."),
            searchPattern: "*.dll")
        .Select(static assemblyPath => MetadataReference.CreateFromFile(assemblyPath))];

    public static Task<Diagnostic[]> Analyze(DiagnosticAnalyzer analyzer, string source, string fileName = "ExampleType.cs", bool allowUnsafeCode = false)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14), fileName);

        return Analyze(analyzer, syntaxTrees: [syntaxTree], allowUnsafeCode);
    }

    public static async Task<Diagnostic[]> Analyze(DiagnosticAnalyzer analyzer, IEnumerable<SyntaxTree> syntaxTrees, bool allowUnsafeCode = false)
    {
        foreach (DiagnosticDescriptor descriptor in analyzer.SupportedDiagnostics)
        {
            Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
            Assert.True(descriptor.IsEnabledByDefault);
            Assert.Contains(WellKnownDiagnosticTags.NotConfigurable, descriptor.CustomTags);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(assemblyName: "AnalyzerTestAssembly",
            syntaxTrees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: allowUnsafeCode));

        Diagnostic[] compilerErrors = [.. compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];

        Assert.Empty(compilerErrors);

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(analyzers: [analyzer])
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(continueOnCapturedContext: false);

        Diagnostic[] reported = [.. diagnostics];

        Assert.DoesNotContain(reported, static diagnostic => diagnostic.Id == "AD0001");

        return reported;
    }
}
