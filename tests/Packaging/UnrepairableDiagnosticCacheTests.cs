using System.Security;

namespace NetAgents.Analyzers.Tests.Packaging;

/// <summary>
/// Verifies a run that ends with diagnostics no fix repairs still records its completed formatting.
/// </summary>
public sealed class UnrepairableDiagnosticCacheTests
{
    // A second summary on one declaration reports NETAGENTS0025, which no code fix repairs, while
    // the argument list below it is something the formatter does rewrite. The run therefore both
    // reformats the file and ends with a diagnostic that fails the build.
    private const string UnrepairableSource = """
        namespace Consumer.Features;

        /// <summary>Greets a recipient.</summary>
        public static class ExampleService
        {
            /// <summary>Returns the supplied recipient name.</summary>
            /// <summary>Describes the same declaration a second time.</summary>
            /// <param name="recipientName">The name shown in the greeting.</param>
            /// <returns>The greeting text.</returns>
            public static string CreateGreeting(
                string recipientName)
            {
                return recipientName;
            }
        }
        """ + "\n";

    /// <summary>
    /// Verifies a second build reuses the formatting of a project the formatter could not make clean.
    /// </summary>
    [Fact]
    public async Task ReusesFormattingThatEndedWithUnrepairableDiagnostics()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory(prefix: "netagents-unrepairable-cache-");

        try
        {
            string packagePath = Environment.GetEnvironmentVariable(variable: "NETAGENTS_PACKAGE_PATH")
                ?? await ConsumerBuild.PackAnalyzer(workspace.FullName);

            string version = PackageArtifact.VerifyPackageContents(packagePath);
            string projectPath = Path.Combine(workspace.FullName, path2: "Consumer.csproj");
            string sourceDirectory = Directory.CreateDirectory(Path.Combine(workspace.FullName, path2: "Features")).FullName;
            string sourcePath = Path.Combine(sourceDirectory, path2: "ExampleService.cs");

            string projectContent = $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <RootNamespace>Consumer</RootNamespace>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="NetAgents.Analyzers" Version="{{SecurityElement.Escape(version)}}" PrivateAssets="all" />
                  </ItemGroup>
                </Project>
                """;

            string sourceConfiguration = $$"""
                <configuration><packageSources><clear />
                <add key="local" value="{{SecurityElement.Escape(Path.GetDirectoryName(Path.GetFullPath(packagePath)))}}" />
                </packageSources></configuration>
                """;

            await File.WriteAllTextAsync(projectPath, projectContent);
            await File.WriteAllTextAsync(Path.Combine(workspace.FullName, path2: "NuGet.Config"), sourceConfiguration);
            await File.WriteAllTextAsync(sourcePath, UnrepairableSource);
            await ConsumerBuild.RunDevelopmentKit(workspace.FullName, ["restore", projectPath, "--packages", Path.Combine(workspace.FullName, path2: "packages")]);

            string[] buildArguments = ["build", projectPath, "--no-restore", "--nologo", "--verbosity", "minimal"];

            // The first build reformats the argument list and then fails on the repeated summary.
            await ConsumerBuild.RunDevelopmentKit(
                workspace.FullName,
                buildArguments,
                expectedDiagnosticIdentifier: "NETAGENTS0025",
                expectedOutput: ["NetAgents rewrote", "analyzing project"],
                withoutShell: true
            );

            string formatted = await File.ReadAllTextAsync(sourcePath);
            Assert.DoesNotContain(expectedSubstring: "CreateGreeting(\n", formatted, StringComparison.Ordinal);

            // The formatting is finished even though the project still reports. A second build must
            // reuse it rather than reanalyze, and must fail on exactly the same diagnostic.
            await ConsumerBuild.RunDevelopmentKit(
                workspace.FullName,
                buildArguments,
                expectedDiagnosticIdentifier: "NETAGENTS0025",
                ["inputs unchanged; reusing completed formatting"],
                ["analyzing project", "NetAgents rewrote"],
                withoutShell: true
            );

            Assert.Equal(formatted, await File.ReadAllTextAsync(sourcePath));

            string cachePath = Assert.Single(Directory.GetFiles(workspace.FullName, searchPattern: "*.NetAgents.formatting.cache", SearchOption.AllDirectories));

            Assert.True(File.Exists(cachePath));

            // Repairing the reported diagnostic is a source edit, so the next build formats again.
            await File.WriteAllTextAsync(
                sourcePath,
                formatted.Replace(
                    oldValue: "    /// <summary>Describes the same declaration a second time.</summary>\n",
                    newValue: "    /// <remarks>Describes the same declaration a second time.</remarks>\n",
                    StringComparison.Ordinal
                )
            );

            await ConsumerBuild.RunDevelopmentKit(workspace.FullName, buildArguments, expectedOutput: ["analyzing project"], withoutShell: true);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }
}
