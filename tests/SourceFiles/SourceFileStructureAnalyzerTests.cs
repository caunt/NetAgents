using NetAgents.Analyzers.SourceFiles;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.SourceFiles;

public sealed class SourceFileStructureAnalyzerTests
{
    [Theory]
    [InlineData("public class DifferentName { }")]
    [InlineData("public class ExampleType { } public class SecondType { }")]
    [InlineData("namespace First { public class ExampleType { } } namespace Second { public class DifferentName { } }")]
    public async Task RejectsMismatchedAndMultipleTopLevelTypes(string source)
    {
        Microsoft.CodeAnalysis.Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new SourceFileStructureAnalyzer(), source);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == SourceFileStructureAnalyzer.RuleIdentifier);
    }

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
