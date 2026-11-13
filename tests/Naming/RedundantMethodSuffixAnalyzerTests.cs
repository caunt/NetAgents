using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Naming;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Naming;

/// <summary>
/// Covers method declarations whose name ends with a word their first parameter's name already says.
/// </summary>
public sealed class RedundantMethodSuffixAnalyzerTests
{
    /// <summary>
    /// Verifies a declaration is accepted when its name does not end with its first parameter's whole name: the
    /// names differ, the name ends with only some of the parameter's words, as in the App Store lookups of
    /// issue 53, or the name is nothing but the parameter's words. Calls are never checked, whatever the
    /// variables they pass are called.
    /// </summary>
    /// <param name="members">The members declared in the type.</param>
    [Theory]
    [InlineData("public void Update(int value) { }")]
    [InlineData("public void UpdateValue(int count) { }")]
    [InlineData("public void UpdateValue() { }")]
    [InlineData("public void Log(string log) { }")]
    [InlineData("public void WithAnalyzer(int[] analyzers) { }")]
    [InlineData("public void Remove(Item item) { }")]
    [InlineData("public void RemoveItem(Item target) { }")]
    [InlineData("public string GetApp(string appStoreId) => appStoreId;")]
    [InlineData("public string? GetFileId(string appStoreId) => null;")]
    [InlineData("public System.Threading.Tasks.Task GetFileIdAsync(string appStoreId, string mirrorId) => System.Threading.Tasks.Task.CompletedTask;")]
    [InlineData("public void ValidateArtifactName(string directoryName, string name) { }")]
    [InlineData("public void UpdateValue(int myValue) { }")]
    [InlineData("public void FromJob(string jobAbc) { }")]
    [InlineData("public string AppStoreId(string appStoreId) => appStoreId;")]
    [InlineData("public void Run(int myValue) { Take(myValue); } public void Take(int value) { }")]
    [InlineData("public void Run(int value) { System.Console.WriteLine(value); }")]
    public async Task AllowsDistinctNames(string members)
    {
        Assert.Empty(await Analyze(members));
    }

    /// <summary>
    /// Verifies the message names every repeated word, so the rename it asks for is the one that drops the
    /// parameter's whole name.
    /// </summary>
    [Fact]
    public async Task NamesTheWholeRepeatedName()
    {
        Diagnostic diagnostic = Assert.Single(await Analyze(members: "public string GetAppStoreId(string appStoreId) => appStoreId;"));

        Assert.StartsWith("Rename 'GetAppStoreId' so it does not end with 'AppStoreId', which its first parameter 'appStoreId' already says: "
            + "drop those words from the method name",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a method taking its name from elsewhere is accepted: an override, an implementation of an
    /// interface member, which is reported on the interface instead, and a framework interface's method.
    /// </summary>
    [Fact]
    public async Task ReportsOnlyWhereTheNameIsDeclared()
    {
        const string members = """
            public interface IUpdater
            {
                void UpdateValue(int value);
            }

            public class Updater : IUpdater, System.IFormattable
            {
                public void UpdateValue(int value) { }
                public string ToString(string? format, System.IFormatProvider? formatProvider) => string.Empty;
                public virtual void SetName(string name) { }
            }

            public sealed class Named : Updater
            {
                public override void SetName(string name) { }
            }
            """;

        Diagnostic[] diagnostics = await AnalyzerTestHarness.Analyze(new RedundantMethodSuffixAnalyzer(), members);

        Assert.Equal(expected: 2, diagnostics.Length);

        Assert.Contains(diagnostics,
            static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(value: "'UpdateValue'", StringComparison.Ordinal));

        Assert.Contains(diagnostics,
            static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(value: "'SetName'", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a declaration is reported when its name ends with its first parameter's whole name, one word or
    /// several and in any case, for methods, async methods, and local functions alike.
    /// </summary>
    /// <param name="members">The members declared in the type.</param>
    [Theory]
    [InlineData("public void RemoveItem(Item item) { }")]
    [InlineData("public void UpdateValue(int value) { }")]
    [InlineData("public System.Threading.Tasks.Task UpdateValueAsync(int value) => System.Threading.Tasks.Task.CompletedTask;")]
    [InlineData("public string GetAppStoreId(string appStoreId) => appStoreId;")]
    [InlineData("public string GetAppStoreID(string appStoreId) => appStoreId;")]
    [InlineData("public void WithAnalyzers(int[] analyzers, int count) { }")]
    [InlineData("public bool ReadValue(out int value) { value = 0; return true; }")]
    [InlineData("public void Run() { void SetName(string name) { } SetName(name: \"entry\"); }")]
    public async Task ReportsRepeatedParameterNames(string members)
    {
        Assert.Equal(RedundantMethodSuffixAnalyzer.RuleIdentifier, Assert.Single(await Analyze(members)).Id);
    }

    private static async Task<Diagnostic[]> Analyze(string members)
    {
        return await AnalyzerTestHarness.Analyze(new RedundantMethodSuffixAnalyzer(), $"public sealed class Item; public sealed class ExampleType {{ {members} }}")
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
