using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.CodeFixes.Formatting;
using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Formatting.Chains;
using NetAgents.Analyzers.Formatting.Clauses;
using NetAgents.Analyzers.Formatting.Statements;
using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting.Clauses;

/// <summary>
/// Runs the formatter on a long value left below its =, and checks the line breaks where the rules say it should: at
/// the links of a chain when the value has one, and at the arguments of its call when it has none.
/// </summary>
public sealed class ValuePlacementConvergenceTests
{
    private const string Method = "using System.Threading.Tasks;\n\nsealed class SerializationContext\n{\n"
        + "    public static SerializationContext Default { get; } = new();\n\n    public object FileEnvelope { get; } = new();\n}\n\n"
        + "static class Serializer\n{\n    public static Task<object> Read(object content, object typeInformation, string label)\n    {\n"
        + "        return Task.FromResult(content);\n    }\n}\n\n"
        + "static class Example\n{\n    static async Task<object> Run(object responseContent)\n    {\n        object envelope;\n\nBODY\n\n"
        + "        return envelope;\n    }\n}\n";

    /// <summary>
    /// Verifies a value with a chain joins its = and splits at the chain, leaving the call's arguments together, while
    /// a value without one joins its = and wraps the arguments, whichever rule runs first.
    /// </summary>
    /// <param name="first">The rule that runs first: the one for values or the one for chains.</param>
    /// <param name="written">The statement as written.</param>
    /// <param name="expected">The statement after the run.</param>
    [Theory]
    [InlineData("values",
        "envelope =\n    await Serializer.Read(responseContent, SerializationContext.Default.FileEnvelope, \"a label long enough to keep this call wide on its line\")\n        .ConfigureAwait(continueOnCapturedContext: false);",
        "envelope = await Serializer.Read(responseContent, SerializationContext.Default.FileEnvelope, \"a label long enough to keep this call wide on its line\")\n    .ConfigureAwait(continueOnCapturedContext: false);")]
    [InlineData("chains",
        "envelope =\n    await Serializer.Read(responseContent, SerializationContext.Default.FileEnvelope, \"a label long enough to keep this call wide on its line\")\n        .ConfigureAwait(continueOnCapturedContext: false);",
        "envelope = await Serializer.Read(responseContent, SerializationContext.Default.FileEnvelope, \"a label long enough to keep this call wide on its line\")\n    .ConfigureAwait(continueOnCapturedContext: false);")]
    [InlineData("values",
        "envelope =\n    await Serializer.Read(responseContent, SerializationContext.Default.FileEnvelope, \"a label long enough to keep this call wide on its line\");",
        "envelope = await Serializer.Read(responseContent,\n    SerializationContext.Default.FileEnvelope,\n    \"a label long enough to keep this call wide on its line\");")]
    [InlineData("chains",
        "envelope =\n    await Serializer.Read(responseContent, SerializationContext.Default.FileEnvelope, \"a label long enough to keep this call wide on its line\");",
        "envelope = await Serializer.Read(responseContent,\n    SerializationContext.Default.FileEnvelope,\n    \"a label long enough to keep this call wide on its line\");")]
    public async Task BreaksLongValuesInsideThemselves(string first, string written, string expected)
    {
        DiagnosticAnalyzer[] analyzers = [new ValuePlacementAnalyzer(), new ArgumentLayoutAnalyzer(), new OneLineStatementAnalyzer(), new ChainLayoutAnalyzer()];

        CodeFixProvider[] providers =
        [
            new ValuePlacementCodeFixProvider(),
            new ArgumentLayoutCodeFixProvider(),
            new OneLineStatementCodeFixProvider(),
            new ChainLayoutCodeFixProvider(),
        ];

        if (first is "chains")
        {
            Array.Reverse(analyzers);
            Array.Reverse(providers);
        }

        using AdhocWorkspace workspace = new();

        Project project = workspace.AddProject(name: "Values", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Example.cs", SourceText.From(Wrap(written)), filePath: "Example.cs")
            .Project;

        CodeFixRunner.CodeFixResult run = await CodeFixRunner.Fix(project,
            analyzers: [.. analyzers],
            providers: [.. providers],
            static fault => Assert.Fail(fault),
            CancellationToken.None);

        string formatted = await DocumentText.Read(run.Project.Documents.Single());

        Assert.Equal(Wrap(expected), formatted);
        Assert.True(run.Blocking.IsEmpty);

        foreach (DiagnosticAnalyzer analyzer in analyzers)
            Assert.Empty(await AnalyzerTestHarness.Analyze(analyzer, formatted));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
