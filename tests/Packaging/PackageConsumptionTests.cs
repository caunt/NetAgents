using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Configuration;

namespace NetAgents.Analyzers.Tests.Packaging;

/// <summary>
/// Verifies the published package and its enforcement in real consumer builds.
/// </summary>
public sealed class PackageConsumptionTests
{
    private const string ValidSource = """
        namespace Consumer.Features;

        /// <summary>Provides the consumer entry point.</summary>
        public static class ExampleService
        {
            /// <summary>Returns the supplied recipient name.</summary>
            /// <param name="recipientName">The name to return.</param>
            /// <returns>The supplied name.</returns>
            public static string CreateGreeting(string recipientName)
            {
                return recipientName;
            }
        }
        """ + "\n";

    /// <summary>
    /// Verifies SDK defaults, consumer diagnostics, and rejected policy overrides.
    /// </summary>
    [Fact]
    public async Task PackageEnforcesPolicyInConsumerBuilds()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory(prefix: "netagents-package-consumer-");

        try
        {
            string packagePath = Environment.GetEnvironmentVariable(variable: "NETAGENTS_PACKAGE_PATH")
                ?? await PackAnalyzer(workspace.FullName);

            packagePath = Path.GetFullPath(packagePath);
            string packageVersion = VerifyPackageContents(packagePath);
            await VerifyConfigurationMatchesTemplate(workspace.FullName, packagePath);
            string projectPath = Path.Combine(paths: [workspace.FullName, "Consumer.csproj"]);
            string sourcePath = Path.Combine(paths: [workspace.FullName, "ExampleService.cs"]);

            string projectContent = $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="NetAgents.Analyzers" Version="{{SecurityElement.Escape(packageVersion)}}" PrivateAssets="all" />
                  </ItemGroup>
                </Project>
                """;

            await File.WriteAllTextAsync(projectPath, projectContent);
            await File.WriteAllTextAsync(sourcePath, ValidSource);

            string packageDirectory = Path.GetDirectoryName(packagePath)
                ?? throw new InvalidOperationException(message: "The package directory is missing.");

            string sourceConfiguration = $$"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="local" value="{{SecurityElement.Escape(packageDirectory)}}" />
                  </packageSources>
                </configuration>
                """;

            await File.WriteAllTextAsync(Path.Combine(paths: [workspace.FullName, "NuGet.Config"]), sourceConfiguration);
            await RunDevelopmentKit(workspace.FullName, arguments:
                ["restore", projectPath, "--packages", Path.Combine(paths: [workspace.FullName, "packages"])]);

            string[] buildArguments = ["build", projectPath, "--no-restore", "--nologo", "--verbosity", "quiet"];
            await RunDevelopmentKit(workspace.FullName, buildArguments);

            string spacingSource = ValidSource.Replace(oldValue: "return recipientName;",
                newValue: "string greeting = recipientName;\n        if (string.IsNullOrEmpty(greeting))\n        {\n            return string.Empty;\n        }\n        return greeting;",
                StringComparison.Ordinal);

            await File.WriteAllTextAsync(sourcePath, spacingSource);
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0016");
            await RunDevelopmentKit(workspace.FullName, arguments:
                ["format", "analyzers", projectPath, "--diagnostics", "NETAGENTS0016", "--no-restore"]);

            string expectedSpacing = spacingSource.Replace(oldValue: "\n        if", newValue: "\n\n        if", StringComparison.Ordinal)
                .Replace(oldValue: "\n        return greeting;", newValue: "\n\n        return greeting;", StringComparison.Ordinal);

            Assert.Equal(expectedSpacing, await File.ReadAllTextAsync(sourcePath));
            await RunDevelopmentKit(workspace.FullName, buildArguments);
            await RunDevelopmentKit(workspace.FullName, arguments:
                ["format", "analyzers", projectPath, "--diagnostics", "NETAGENTS0016", "--no-restore", "--verify-no-changes"]);

            string lookupSource = ValidSource.Replace(oldValue: "return recipientName;",
                newValue: "System.Collections.Generic.Dictionary<string, string> values = [];\n\n"
                    + "        return values.TryGetValue(recipientName, out string? greeting) ? greeting : recipientName;",
                StringComparison.Ordinal);

            await File.WriteAllTextAsync(sourcePath, lookupSource);
            await RunDevelopmentKit(workspace.FullName, buildArguments);
            await File.WriteAllTextAsync(sourcePath, lookupSource.Replace(
                oldValue: "return values.TryGetValue(recipientName, out string? greeting) ? greeting : recipientName;",
                newValue: "bool found = values.TryGetValue(recipientName, out string? greeting);\n\n        return greeting ?? recipientName;",
                StringComparison.Ordinal));
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "IDE0059");

            string[] ignoredLookups =
            [
                "values.TryGetValue(recipientName, out string? greeting);",
                "_ = values.TryGetValue(recipientName, out string? greeting);",
            ];

            foreach (string ignoredLookup in ignoredLookups)
            {
                string ignoredSource = lookupSource.Replace(
                    oldValue: "return values.TryGetValue(recipientName, out string? greeting) ? greeting : recipientName;",
                    newValue: ignoredLookup + "\n\n        return greeting ?? recipientName;", StringComparison.Ordinal);

                await File.WriteAllTextAsync(sourcePath, ignoredSource);
                await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0015");
                await File.WriteAllTextAsync(sourcePath, "#pragma warning disable NETAGENTS0015\n" + ignoredSource);
                await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0015");
            }

            string boxingSource = ValidSource.Replace(oldValue: "string CreateGreeting(string recipientName)",
                newValue: "System.IComparable CreateGreeting(int recipientName)", StringComparison.Ordinal);

            await File.WriteAllTextAsync(sourcePath, boxingSource);
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0001");
            await File.WriteAllTextAsync(sourcePath, "#pragma warning disable NETAGENTS0001\n" + boxingSource);
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0001");
            await File.WriteAllTextAsync(sourcePath, ValidSource.Replace(oldValue: "        return recipientName;",
                newValue: "return recipientName;", StringComparison.Ordinal));
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "IDE0055");
            await File.WriteAllTextAsync(sourcePath, "using System.Text;\n\n" + ValidSource);
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "IDE0005");
            await File.WriteAllTextAsync(sourcePath, ValidSource.Replace(
                oldValue: "/// <summary>Provides the consumer entry point.</summary>\n", newValue: string.Empty, StringComparison.Ordinal));
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "CS1591");
            await File.WriteAllTextAsync(sourcePath, ValidSource.Replace(oldValue: "return recipientName;",
                newValue: "return recipientName.ToLower();", StringComparison.Ordinal));
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "CA1311");
            await File.WriteAllTextAsync(sourcePath, ValidSource);

            string editorConfigurationPath = Path.Combine(paths: [workspace.FullName, ".editorconfig"]);

            string[] conflictingEditorSettings =
            [
                "csharp_style_var_elsewhere = true:silent",
                "insert_final_newline = false",
                "dotnet_diagnostic.NETAGENTS0001.severity = none",
                "dotnet_diagnostic.NETAGENTS0015.severity = none",
                "dotnet_diagnostic.IDE0005.severity = none",
                "dotnet_diagnostic.IDE0059.severity = none",
                "dotnet_diagnostic.CS1591.severity = none",
            ];

            foreach (string conflictingSetting in conflictingEditorSettings)
            {
                await File.WriteAllTextAsync(editorConfigurationPath, $"root = true\n[*.cs]\n{conflictingSetting}\n");
                await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0013");
            }

            File.Delete(editorConfigurationPath);

            string[] conflictingProjectSettings =
            [
                "<Nullable>disable</Nullable>",
                "<AnalysisLevel>none</AnalysisLevel>",
                "<AnalysisMode>Minimum</AnalysisMode>",
                "<AnalysisLevelStyle>none</AnalysisLevelStyle>",
                "<AnalysisModeStyle>None</AnalysisModeStyle>",
                "<CodeAnalysisTreatWarningsAsErrors>false</CodeAnalysisTreatWarningsAsErrors>",
                "<EnableCodeStyleSeverity>false</EnableCodeStyleSeverity>",
                "<EnableNETAnalyzers>false</EnableNETAnalyzers>",
                "<RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>",
                "<TreatWarningsAsErrors>false</TreatWarningsAsErrors>",
                "<GenerateDocumentationFile>false</GenerateDocumentationFile>",
                "<WarningsAsErrors>CS0168</WarningsAsErrors>",
                "<EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>",
            ];

            foreach (string conflictingSetting in conflictingProjectSettings)
            {
                await File.WriteAllTextAsync(projectPath, projectContent.Replace(oldValue: "<ImplicitUsings>enable</ImplicitUsings>",
                    newValue: $"<ImplicitUsings>enable</ImplicitUsings>{conflictingSetting}", StringComparison.Ordinal));
                await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0014");
            }

            await File.WriteAllTextAsync(projectPath, projectContent);

            string[] conflictingBuildArguments =
            [
                "-p:RunAnalyzers=false",
                "-p:CodeAnalysisTreatWarningsAsErrors=false",
                "-p:GenerateDocumentationFile=false",
                "-p:SkipGlobalAnalyzerConfigForPackage=true",
                "-p:NoWarn=NETAGENTS0001",
                "-p:NoWarn=NETAGENTS0015",
                "-p:NoWarn=1591",
                "-p:WarningsNotAsErrors=CS1591",
                "-p:NoWarn=IDE0005",
                "-p:WarningsNotAsErrors=CA1311",
            ];

            foreach (string conflictingArgument in conflictingBuildArguments)
            {
                await RunDevelopmentKit(workspace.FullName, arguments: [.. buildArguments, conflictingArgument],
                    expectedDiagnosticIdentifier: "NETAGENTS0014");
            }

            await RunDevelopmentKit(workspace.FullName, buildArguments);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    private static string VerifyPackageContents(string packagePath)
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
        {
            Assert.NotNull(package.GetEntry(requiredFile));
        }

        Assert.Equal(expected: 2, actual: package.Entries.Count(static entry => entry.FullName.EndsWith(value: ".dll", StringComparison.Ordinal)));

        ZipArchiveEntry manifestEntry = package.GetEntry(entryName: "NetAgents.Analyzers.nuspec")
            ?? throw new InvalidOperationException(message: "The package manifest is missing.");

        using Stream manifestStream = manifestEntry.Open();

        XDocument manifest = XDocument.Load(manifestStream);
        Assert.DoesNotContain(manifest.Descendants(), static element => element.Name.LocalName == "dependency");

        return manifest.Descendants().Single(static element => element.Name.LocalName == "version").Value;
    }

    private static async Task VerifyConfigurationMatchesTemplate(string workspacePath, string packagePath)
    {
        string templateDirectory = Path.Combine(paths: [workspacePath, "microsoft-defaults"]);
        await RunDevelopmentKit(workspacePath, arguments: ["new", "editorconfig", "--output", templateDirectory])
            .ConfigureAwait(continueOnCapturedContext: false);
        string templatePath = Path.Combine(paths: [templateDirectory, ".editorconfig"]);
        string template = await File.ReadAllTextAsync(templatePath).ConfigureAwait(continueOnCapturedContext: false);
        AnalyzerConfig[] templateConfigurations = [AnalyzerConfig.Parse(template, templatePath)];

        AnalyzerConfigOptionsResult templateOptions = AnalyzerConfigSet.Create(templateConfigurations)
            .GetOptionsForSourcePath(Path.Combine(paths: [templateDirectory, "ExampleService.cs"]));

        Assert.NotEmpty(templateOptions.AnalyzerOptions);

        using Stream embeddedStream = typeof(ConfigurationPolicyAnalyzer).Assembly.GetManifestResourceStream(
            name: "NetAgents.Analyzers.Configuration.NetAgents.globalconfig")
            ?? throw new InvalidOperationException(message: "The embedded configuration is missing.");

        using StreamReader embeddedReader = new(embeddedStream);

        string embeddedConfiguration = await embeddedReader.ReadToEndAsync().ConfigureAwait(continueOnCapturedContext: false);
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
                _ => preference.Value,
            };

            Assert.Equal(expectedValue, generatedOptions.AnalyzerOptions[preference.Key]);
        }
    }

    private static async Task<string> PackAnalyzer(string workspacePath)
    {
        DirectoryInfo? repositoryDirectory = new(AppContext.BaseDirectory);

        while (repositoryDirectory is not null
            && !File.Exists(Path.Combine(paths: [repositoryDirectory.FullName, "NetAgents.slnx"])))
        {
            repositoryDirectory = repositoryDirectory.Parent;
        }

        Assert.NotNull(repositoryDirectory);

        string configuration = typeof(PackageConsumptionTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            ?? throw new InvalidOperationException(message: "The test build configuration is missing.");

        string packageDirectory = Path.Combine(paths: [workspacePath, "artifacts"]);
        await RunDevelopmentKit(repositoryDirectory.FullName, arguments:
        [
            "pack", "src/NetAgents.Analyzers.csproj", "--configuration", configuration,
            "--no-build", "--no-restore", "--output", packageDirectory,
        ]).ConfigureAwait(continueOnCapturedContext: false);

        return Assert.Single(Directory.EnumerateFiles(packageDirectory, searchPattern: "*.nupkg"));
    }

    private static async Task RunDevelopmentKit(string workingDirectory, string[] arguments,
        string? expectedDiagnosticIdentifier = null)
    {
        using Process process = new();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable(variable: "DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(minutes: 2)).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(continueOnCapturedContext: false);

            throw;
        }

        string output = await standardOutput.ConfigureAwait(continueOnCapturedContext: false) + await standardError.ConfigureAwait(continueOnCapturedContext: false);
        Assert.DoesNotContain(expectedSubstring: "AD0001", actualString: output, StringComparison.Ordinal);

        if (expectedDiagnosticIdentifier is null)
        {
            Assert.True(process.ExitCode == 0, output);
        }
        else
        {
            Assert.True(process.ExitCode != 0, output);
            Assert.Contains(expectedDiagnosticIdentifier, output, StringComparison.Ordinal);
        }
    }
}
