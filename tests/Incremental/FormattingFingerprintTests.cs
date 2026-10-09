using NetAgents.BuildTasks;

using TaskItem = Microsoft.Build.Utilities.TaskItem;

namespace NetAgents.Analyzers.Tests.Incremental;

/// <summary>Checks that persisted formatting is reusable only for identical effective inputs.</summary>
public sealed class FormattingFingerprintTests
{
    /// <summary>Detects edits even when file sizes and modification times are preserved.</summary>
    /// <param name="relativePath">The input or loadable dependency to edit.</param>
    [Theory]
    [InlineData("Source.cs")]
    [InlineData("Generated.g.cs")]
    [InlineData("references/Library.dll")]
    [InlineData("analyzers/Rules.dll")]
    [InlineData("analyzers/Dependency.dll")]
    [InlineData("fixes/StyleFixes.dll")]
    [InlineData("sdk/Microsoft.CodeAnalysis.dll")]
    [InlineData(".editorconfig")]
    [InlineData("Additional.txt")]
    [InlineData("project.assets.json")]
    [InlineData("Directory.Build.props")]
    public async Task InvalidatesOnContentChanges(string relativePath)
    {
        using Fixture fixture = new();

        FormattingFingerprint before = await fixture.Read();
        string path = Path.Combine(fixture.Directory.FullName, relativePath);
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        long length = new FileInfo(path).Length;
        await File.WriteAllTextAsync(path, contents: "after!");
        File.SetLastWriteTimeUtc(path, timestamp);

        Assert.Equal(length, new FileInfo(path).Length);
        Assert.NotEqual(before, await fixture.Read());
    }

    /// <summary>Detects item additions and removals, including missing dependencies becoming available.</summary>
    /// <param name="change">The item-list change to apply.</param>
    [Theory]
    [InlineData("source-added")]
    [InlineData("source-removed")]
    [InlineData("source-renamed")]
    [InlineData("reference-removed")]
    [InlineData("analyzer-removed")]
    [InlineData("configuration-added")]
    [InlineData("additional-removed")]
    [InlineData("dependency-added")]
    [InlineData("dependency-removed")]
    public async Task InvalidatesOnInputSetChanges(string change)
    {
        using Fixture fixture = new();

        FormattingFingerprint before = await fixture.Read();

        switch (change)
        {
            case "source-added":
                {
                    fixture.Inputs.SourceFiles = [.. fixture.Inputs.SourceFiles, fixture.Item(relativePath: "Extra.cs")];

                    break;
                }
            case "source-removed":
                {
                    fixture.Inputs.SourceFiles = [fixture.Inputs.SourceFiles[0]];

                    break;
                }
            case "source-renamed":
                {
                    fixture.Inputs.SourceFiles = [fixture.Item(relativePath: "Renamed.cs"), fixture.Inputs.SourceFiles[1]];

                    break;
                }
            case "reference-removed":
                {
                    fixture.Inputs.References = [];

                    break;
                }
            case "analyzer-removed":
                {
                    fixture.Inputs.AnalyzerFiles = [];

                    break;
                }
            case "configuration-added":
                {
                    fixture.Inputs.ConfigurationFiles = [.. fixture.Inputs.ConfigurationFiles, fixture.Item(relativePath: "Other.globalconfig")];

                    break;
                }
            case "additional-removed":
                {
                    fixture.Inputs.AdditionalFiles = [];

                    break;
                }
            case "dependency-added":
                {
                    await File.WriteAllTextAsync(Path.Combine(fixture.Directory.FullName, path2: "analyzers/New.dll"), contents: "before");

                    break;
                }
            case "dependency-removed":
                {
                    File.Delete(Path.Combine(fixture.Directory.FullName, path2: "analyzers/Dependency.dll"));

                    break;
                }
            default:
                throw new ArgumentException(message: "Unknown input change.", nameof(change));
        }

        Assert.NotEqual(before, await fixture.Read());
    }

    /// <summary>Includes compiler settings and the metadata controlling source layout and binding.</summary>
    /// <param name="setting">The evaluated setting to change.</param>
    [Theory]
    [InlineData("assembly-name")]
    [InlineData("root-namespace")]
    [InlineData("language-version")]
    [InlineData("symbols")]
    [InlineData("unsafe")]
    [InlineData("output-kind")]
    [InlineData("source-link")]
    [InlineData("reference-aliases")]
    [InlineData("embed-interop-types")]
    [InlineData("compiler-option")]
    public async Task InvalidatesOnSettingsChanges(string setting)
    {
        using Fixture fixture = new();

        FormattingFingerprint before = await fixture.Read();

        switch (setting)
        {
            case "assembly-name":
                {
                    fixture.Inputs.AssemblyName = "Different";

                    break;
                }
            case "root-namespace":
                {
                    fixture.Inputs.RootNamespace = "Different";

                    break;
                }
            case "language-version":
                {
                    fixture.Inputs.LanguageVersion = "preview";

                    break;
                }
            case "symbols":
                {
                    fixture.Inputs.DefineConstants = "WINDOWS";

                    break;
                }
            case "unsafe":
                {
                    fixture.Inputs.AllowUnsafe = true;

                    break;
                }
            case "output-kind":
                {
                    fixture.Inputs.OutputType = "Exe";

                    break;
                }
            case "source-link":
                {
                    fixture.Inputs.SourceFiles[0].SetMetadata(metadataName: "Link", metadataValue: "Linked/Source.cs");

                    break;
                }
            case "reference-aliases":
                {
                    fixture.Inputs.References[0].SetMetadata(metadataName: "Aliases", metadataValue: "External");

                    break;
                }
            case "embed-interop-types":
                {
                    fixture.Inputs.References[0].SetMetadata(metadataName: "EmbedInteropTypes", metadataValue: "true");

                    break;
                }
            case "compiler-option":
                {
                    fixture.Inputs.BuildProperties[0].SetMetadata(metadataName: "Optimize", metadataValue: "true");

                    break;
                }
            default:
                throw new ArgumentException(message: "Unknown setting.", nameof(setting));
        }

        Assert.NotEqual(before, await fixture.Read());
    }

    /// <summary>Touching an input does not undo completed formatting; changing its content does.</summary>
    [Fact]
    public async Task ReusesIdenticalContentAcrossReads()
    {
        using Fixture fixture = new();

        FormattingFingerprint before = await fixture.Read();
        string path = fixture.Inputs.SourceFiles[0].ItemSpec;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(value: 1));

        Assert.Equal(before, await fixture.Read());
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Inputs.ProjectPath = Path.Combine(Directory.FullName, path2: "Consumer.csproj");
            Inputs.AssemblyName = "Consumer";
            Inputs.SourceFiles = [Item(relativePath: "Source.cs"), Item(relativePath: "Generated.g.cs")];
            Inputs.References = [Item(relativePath: "references/Library.dll")];
            Inputs.AnalyzerFiles = [Item(relativePath: "analyzers/Rules.dll")];
            Inputs.ConfigurationFiles = [Item(relativePath: ".editorconfig")];
            Inputs.AdditionalFiles = [Item(relativePath: "Additional.txt")];
            Inputs.BuildFiles = [Item(relativePath: "project.assets.json"), Item(relativePath: "Directory.Build.props")];
            Inputs.BuildProperties = [new TaskItem(itemSpec: "Compiler")];
            Inputs.CodeFixDirectory = Path.GetDirectoryName(Item(relativePath: "fixes/StyleFixes.dll").ItemSpec) ?? string.Empty;
            Inputs.SdkAssemblyDirectory = Path.GetDirectoryName(Item(relativePath: "sdk/Microsoft.CodeAnalysis.dll").ItemSpec) ?? string.Empty;
            TaskItem dependency = Item(relativePath: "analyzers/Dependency.dll");
            Assert.True(File.Exists(dependency.ItemSpec));
        }

        public DirectoryInfo Directory { get; } = System.IO.Directory.CreateTempSubdirectory(prefix: "netagents-fingerprint-");

        public FormatProject Inputs { get; } = new();

        public void Dispose()
        {
            Inputs.Dispose();
            Directory.Delete(recursive: true);
        }

        public TaskItem Item(string relativePath)
        {
            string path = Path.Combine(Directory.FullName, relativePath);
            FileInfo file = new(path);
            file.Directory?.Create();
            File.WriteAllText(path, contents: "before");

            return new(path);
        }

        public async Task<FormattingFingerprint> Read()
        {
            Dictionary<string, byte[]> sources = await FormattingFingerprint.ReadSources(Inputs.SourceFiles, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

            return await FormattingFingerprint.Create(Inputs, sources, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
        }
    }
}
