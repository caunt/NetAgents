using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Configuration;

namespace NetAgents.Analyzers.Tests.Packaging;

/// <summary>
/// Checks the published package itself: the files it carries and the configuration it embeds.
/// </summary>
internal static class PackageArtifact
{
    internal static async Task VerifyConfigurationMatchesTemplate(string workspacePath, string packagePath)
    {
        string templateDirectory = Path.Combine(paths: [workspacePath, "microsoft-defaults"]);
        await ConsumerBuild.RunDevelopmentKit(workspacePath, ["new", "editorconfig", "--output", templateDirectory])
            .ConfigureAwait(continueOnCapturedContext: false);
        string templatePath = Path.Combine(paths: [templateDirectory, ".editorconfig"]);
        string template = await File.ReadAllTextAsync(templatePath).ConfigureAwait(continueOnCapturedContext: false);
        AnalyzerConfig[] templateConfigurations = [AnalyzerConfig.Parse(template, templatePath)];

        AnalyzerConfigOptionsResult templateOptions = AnalyzerConfigSet.Create(templateConfigurations)
            .GetOptionsForSourcePath(Path.Combine(paths: [templateDirectory, "ExampleService.cs"]));

        Assert.NotEmpty(templateOptions.AnalyzerOptions);

        using Stream embeddedStream = typeof(ConfigurationPolicyAnalyzer).Assembly.GetManifestResourceStream(name: "NetAgents.Analyzers.Configuration.NetAgents.globalconfig")
            ?? throw new InvalidOperationException(message: "The embedded configuration is missing.");

        using StreamReader embeddedReader = new(embeddedStream);

        string embeddedConfiguration = await embeddedReader.ReadToEndAsync().ConfigureAwait(continueOnCapturedContext: false);

        // One global config must not define a key twice; the effective value would depend on parse order.
        List<string> configurationKeys = [];

        foreach (string line in embeddedConfiguration.Split(separator: '\n'))
        {
            int separatorIndex = line.IndexOf(value: '=', StringComparison.Ordinal);

            if (separatorIndex > 0)
                configurationKeys.Add(line[..separatorIndex].Trim());
        }

        Assert.Equal(configurationKeys.Count, configurationKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        ZipArchive package = await ZipFile.OpenReadAsync(packagePath).ConfigureAwait(continueOnCapturedContext: false);

        await using (package.ConfigureAwait(continueOnCapturedContext: false))
        {
            ZipArchiveEntry configurationEntry = package.GetEntry(entryName: "buildTransitive/NetAgents.globalconfig")
                ?? throw new InvalidOperationException(message: "The packaged configuration is missing.");

            using StreamReader packagedReader = new(await configurationEntry.OpenAsync().ConfigureAwait(continueOnCapturedContext: false));

            Assert.Equal(embeddedConfiguration, await packagedReader.ReadToEndAsync().ConfigureAwait(continueOnCapturedContext: false));
        }

        AnalyzerConfig[] generatedConfigurations =
        [
            AnalyzerConfig.Parse(embeddedConfiguration, Path.Combine(paths: [templateDirectory, "NetAgents.globalconfig"])),
        ];

        AnalyzerConfigSet generatedConfiguration = AnalyzerConfigSet.Create(generatedConfigurations, out ImmutableArray<Diagnostic> diagnostics);
        Assert.True(diagnostics.IsEmpty);

        // A source outside the configuration's directory must still receive every C# default.
        AnalyzerConfigOptionsResult generatedOptions = generatedConfiguration
            .GetOptionsForSourcePath(Path.Combine(paths: [workspacePath, "Unrelated", "ExampleService.cs"]));

        Assert.True(generatedOptions.Diagnostics.IsEmpty);

        foreach (KeyValuePair<string, string> preference in templateOptions.AnalyzerOptions)
        {
            // Preserve policy overrides while following the SDK's formatting preferences.
            string expectedValue = preference.Key switch
            {
                "insert_final_newline" => "true",
                "dotnet_naming_symbols.type_parameters.applicable_kinds" when preference.Value == "namespace" => "type_parameter",
                "csharp_style_unused_value_assignment_preference" => preference.Value.Split(separator: ':').First() + ":error",
                "csharp_prefer_braces" => "false:silent",
                _ => preference.Value,
            };

            Assert.Equal(expectedValue, generatedOptions.AnalyzerOptions[preference.Key]);
        }
    }

    internal static string VerifyPackageContents(string packagePath)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);

        string[] requiredFiles =
        [
            "analyzers/dotnet/cs/NetAgents.Analyzers.dll",
            "analyzers/dotnet/cs/NetAgents.CodeFixes.dll",
            "buildTransitive/NetAgents.Analyzers.props",
            "buildTransitive/NetAgents.Analyzers.targets",
            "buildTransitive/NetAgents.globalconfig",
            "README.md",
            "LICENSE",
        ];

        foreach (string requiredFile in requiredFiles)
            Assert.NotNull(package.GetEntry(requiredFile));

        Assert.Equal(
            expected: 2,
            package.Entries.Count(
                static entry => entry.FullName.StartsWith(value: "analyzers/", StringComparison.Ordinal)
            && entry.FullName.EndsWith(value: ".dll", StringComparison.Ordinal)
            )
        );
        Assert.NotNull(package.GetEntry(entryName: "tools/net10.0/NetAgents.BuildTasks.dll"));
        Assert.NotNull(package.GetEntry(entryName: "tools/net10.0/NetAgents.Formatter.dll"));
        Assert.NotNull(package.GetEntry(entryName: "tools/net10.0/Microsoft.CodeAnalysis.Workspaces.dll"));

        ZipArchiveEntry manifestEntry = package.GetEntry(entryName: "NetAgents.Analyzers.nuspec")
            ?? throw new InvalidOperationException(message: "The package manifest is missing.");

        using Stream manifestStream = manifestEntry.Open();

        XDocument manifest = XDocument.Load(manifestStream);
        Assert.DoesNotContain(manifest.Descendants(), static element => element.Name.LocalName == "dependency");

        return manifest.Descendants().Single(static element => element.Name.LocalName == "version").Value;
    }
}
