using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Xml.Linq;

namespace NetAgents.Analyzers.Tests.Packaging;

public sealed class PackageConsumptionTests
{
    private const string ValidSource = """
        namespace Consumer.Features;

        public static class ExampleService
        {
            public static string CreateGreeting(string recipientName)
            {
                return recipientName;
            }
        }
        """;

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

            string boxingSource = ValidSource.Replace(oldValue: "string CreateGreeting(string recipientName)",
                newValue: "System.IComparable CreateGreeting(int recipientName)", StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, boxingSource);
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0001");
            await File.WriteAllTextAsync(sourcePath, "#pragma warning disable NETAGENTS0001\n" + boxingSource);
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0001");
            await File.WriteAllTextAsync(sourcePath, ValidSource.Replace(oldValue: "        return recipientName;",
                newValue: "return recipientName;", StringComparison.Ordinal));
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "IDE0055");
            await File.WriteAllTextAsync(sourcePath, ValidSource);

            string editorConfigurationPath = Path.Combine(paths: [workspace.FullName, ".editorconfig"]);
            await File.WriteAllTextAsync(editorConfigurationPath,
                contents: "root = true\n[*.cs]\ncsharp_style_var_elsewhere = true:silent\n");
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0013");
            await File.WriteAllTextAsync(editorConfigurationPath,
                contents: "root = true\n[*.cs]\ndotnet_diagnostic.NETAGENTS0001.severity = none\n");
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0013");
            File.Delete(editorConfigurationPath);

            await File.WriteAllTextAsync(projectPath, projectContent.Replace(oldValue: "<ImplicitUsings>enable</ImplicitUsings>",
                newValue: "<ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>false</TreatWarningsAsErrors>",
                StringComparison.Ordinal));
            await RunDevelopmentKit(workspace.FullName, buildArguments, expectedDiagnosticIdentifier: "NETAGENTS0014");
            await File.WriteAllTextAsync(projectPath, projectContent);
            await RunDevelopmentKit(workspace.FullName, arguments: [.. buildArguments, "-p:RunAnalyzers=false"],
                expectedDiagnosticIdentifier: "NETAGENTS0014");
            await RunDevelopmentKit(workspace.FullName, arguments: [.. buildArguments, "-p:NoWarn=NETAGENTS0001"],
                expectedDiagnosticIdentifier: "NETAGENTS0014");
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

        ZipArchiveEntry assemblyEntry = Assert.Single(package.Entries, static entry =>
            entry.FullName.EndsWith(value: ".dll", StringComparison.Ordinal));
        Assert.Equal(expected: "analyzers/dotnet/cs/NetAgents.Analyzers.dll", actual: assemblyEntry.FullName);
        ZipArchiveEntry manifestEntry = package.GetEntry(entryName: "NetAgents.Analyzers.nuspec")
            ?? throw new InvalidOperationException(message: "The package manifest is missing.");
        using Stream manifestStream = manifestEntry.Open();
        XDocument manifest = XDocument.Load(manifestStream);
        Assert.DoesNotContain(manifest.Descendants(), static element => element.Name.LocalName == "dependency");
        return manifest.Descendants().Single(static element => element.Name.LocalName == "version").Value;
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
