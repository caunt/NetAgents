using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.Analyzers.Tests.Infrastructure;

internal static class AnalyzerTestHarness
{
    private static readonly MetadataReference[] References = [.. Directory
        .EnumerateFiles(Path.GetDirectoryName(typeof(string).Assembly.Location)
            ?? throw new InvalidOperationException(message: "The runtime assembly directory is unavailable."), searchPattern: "*.dll")
        .Select(static assemblyPath => MetadataReference.CreateFromFile(assemblyPath))];

    public static async Task<Diagnostic[]> Analyze(DiagnosticAnalyzer analyzer, string source, string fileName = "ExampleType.cs")
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source,
            options: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14), path: fileName);
        CSharpCompilation compilation = CSharpCompilation.Create(assemblyName: "AnalyzerTestAssembly",
            syntaxTrees: [syntaxTree], references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        Diagnostic[] compilerErrors = [.. compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Assert.Empty(compilerErrors);

        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync().ConfigureAwait(continueOnCapturedContext: false);
        Diagnostic[] result = [.. diagnostics];
        Assert.DoesNotContain(result, static diagnostic => diagnostic.Id == "AD0001");
        return result;
    }
}