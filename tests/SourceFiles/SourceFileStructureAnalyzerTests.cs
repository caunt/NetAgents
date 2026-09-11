using NetAgents.Analyzers.SourceFiles;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.SourceFiles;

/// <summary>
/// Covers type placement and source file naming requirements.
/// </summary>
public sealed class SourceFileStructureAnalyzerTests
{
    /// <summary>
    /// Verifies that mismatched filenames and multiple top-level types are reported.
    /// </summary>
    /// <param name="source">The source containing invalid type placement.</param>
    [Theory]
    [InlineData("public class DifferentName { }")]
    [InlineData("public class ExampleType { } public class SecondType { }")]
    [InlineData("namespace First { public class ExampleType { } } namespace Second { public class DifferentName { } }")]
    public async Task RejectsMismatchedAndMultipleTopLevelTypes(string source)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new SourceFileStructureAnalyzer(), source);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == SourceFileStructureAnalyzer.RuleIdentifier);
    }

    /// <summary>
    /// Verifies supported top-level declarations and appropriate nested types.
    /// </summary>
    /// <param name="source">The declaration to compile under its matching filename.</param>
    [Theory]
    [InlineData("public class ExampleType { private class NestedType { } }")]
    [InlineData("public record ExampleType(string Name);")]
    [InlineData("public delegate void ExampleType();")]
    public async Task AllowsMatchingFilesAndAppropriateNestedTypes(string source)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new SourceFileStructureAnalyzer(), source);

        Assert.Empty(diagnostics);
    }
}
