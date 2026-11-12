using System.Security;

namespace NetAgents.Analyzers.Tests.Packaging;

/// <summary>
/// Verifies consumer builds give embedded statements and statement blocks lines of their own (#49).
/// </summary>
public sealed class StatementLayoutTests
{
    // The code from issue #49, and a lambda whose statement must spread over lines once laid out.
    private const string Crowded = """
        namespace Consumer;

        /// <summary>Holds a connection until it is disposed.</summary>
        public sealed class Connection : IAsyncDisposable
        {
            private int _disposed;

            /// <summary>Starts the connection in the background.</summary>
            /// <param name="ready">Whether the connection may start.</param>
            /// <returns>The background work.</returns>
            public static Task StartAsync(bool ready)
            {
                return Task.Run(() => { if (ready) Console.WriteLine(value: "ready"); });
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

                try { await DisconnectAsync().ConfigureAwait(continueOnCapturedContext: false); }
                catch (InvalidOperationException exception) { Log(exception); }
            }

            private static Task DisconnectAsync()
            {
                return Task.CompletedTask;
            }

            private static void Log(InvalidOperationException exception)
            {
                Console.WriteLine(exception.Message);
            }
        }

        """;

    private const string Expected = """
        namespace Consumer;

        /// <summary>Holds a connection until it is disposed.</summary>
        public sealed class Connection : IAsyncDisposable
        {
            private int _disposed;

            /// <summary>Starts the connection in the background.</summary>
            /// <param name="ready">Whether the connection may start.</param>
            /// <returns>The background work.</returns>
            public static Task StartAsync(bool ready)
            {
                return Task.Run(() =>
                {
                    if (ready)
                        Console.WriteLine(value: "ready");
                });
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, value: 1) != 0)
                    return;

                try
                {
                    await DisconnectAsync().ConfigureAwait(continueOnCapturedContext: false);
                }
                catch (InvalidOperationException exception)
                {
                    Log(exception);
                }
            }

            private static Task DisconnectAsync()
            {
                return Task.CompletedTask;
            }

            private static void Log(InvalidOperationException exception)
            {
                Console.WriteLine(exception.Message);
            }
        }

        """;

    /// <summary>
    /// Verifies a build rewrites one-line statements and blocks into the issue's layout, and a second
    /// build leaves that layout as it is.
    /// </summary>
    [Fact]
    public async Task PackageGivesStatementsTheirOwnLines()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory(prefix: "netagents-package-statements-");

        try
        {
            string packagePath = Environment.GetEnvironmentVariable(variable: "NETAGENTS_PACKAGE_PATH")
                ?? await ConsumerBuild.PackAnalyzer(workspace.FullName);

            string version = PackageArtifact.VerifyPackageContents(packagePath);
            string projectPath = Path.Combine(workspace.FullName, path2: "Consumer.csproj");
            string sourcePath = Path.Combine(workspace.FullName, path2: "Connection.cs");

            string projectContent = $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
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
            await File.WriteAllTextAsync(sourcePath, Crowded);
            await ConsumerBuild.RunDevelopmentKit(workspace.FullName, ["restore", projectPath, "--packages", Path.Combine(workspace.FullName, path2: "packages")]);
            string[] buildArguments = ["build", projectPath, "--no-restore", "--nologo", "--verbosity", "quiet"];

            await ConsumerBuild.RunDevelopmentKit(workspace.FullName, buildArguments, forbiddenOutput: ["NetAgents automatic formatting failed"]);
            Assert.Equal(Expected, await File.ReadAllTextAsync(sourcePath));

            await ConsumerBuild.RunDevelopmentKit(workspace.FullName, buildArguments, forbiddenOutput: ["NetAgents automatic formatting failed"]);
            Assert.Equal(Expected, await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }
}
