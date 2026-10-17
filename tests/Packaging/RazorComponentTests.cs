using System.Security;

namespace NetAgents.Analyzers.Tests.Packaging;

/// <summary>
/// Verifies the C# written in Razor components meets the rules C# files do, in real consumer builds.
/// </summary>
public sealed class RazorComponentTests
{
    // Markup Razor renders through generated calls: a boxed int, attributes, handlers, binding, and
    // control flow, around a code block that breaks no rule.
    private const string Counter = """
        @page "/counter"
        @using Microsoft.AspNetCore.Components.Web
        @inject NavigationManager Navigation

        <h1>@Title</h1>
        <p role="status" class="@statusClass">Current count: @count</p>
        <input @bind="message" />

        @if (count > 0)
        {
            <p>@message (@count.ToString(System.Globalization.CultureInfo.InvariantCulture))</p>
        }

        <ul>
            @foreach (string entry in entries)
            {
                <li @key="entry">@entry</li>
            }
        </ul>

        <button class="btn" @onclick="Increment">Increment</button>
        <button @onclick="() => count = 0">Reset</button>
        <button @onclick="ResetAsync">Reset later</button>
        <a href="@Navigation.BaseUri">Home</a>

        @code {
            private readonly List<string> entries = ["first", "second"];
            private int count;
            private string message = string.Empty;
            private string statusClass = "status";

            [Parameter]
            public string Title { get; set; } = "Counter";

            private void Increment()
            {
                count++;
                statusClass = count > 1 ? "status busy" : "status";
            }

            private async Task ResetAsync()
            {
                await Task.Yield();
                count = 0;
            }
        }

        """;

    // The component from issue #37, whose code block every build used to let through.
    private const string Sample = """
        <p>@_status</p>

        @code {
            private string _status = string.Empty;

            private void Report(string status)
            {
                _status = status;
                _ = InvokeAsync(StateHasChanged);
            }

            private static async Task RunAsync(Func<Task> action)
            {
                try { await action(); }
                catch (InvalidOperationException) { }
            }
        }

        """;

    /// <summary>
    /// Verifies a component builds cleanly around the code Razor generates for its markup, and fails on
    /// the lines of its code block that break a rule, without the build rewriting either file.
    /// </summary>
    [Fact]
    public async Task PackageChecksRazorComponentCode()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory(prefix: "netagents-package-razor-");

        try
        {
            string packagePath = Environment.GetEnvironmentVariable(variable: "NETAGENTS_PACKAGE_PATH")
                ?? await ConsumerBuild.PackAnalyzer(workspace.FullName);

            string version = PackageArtifact.VerifyPackageContents(packagePath);
            string projectPath = Path.Combine(workspace.FullName, path2: "Components.csproj");
            string counterPath = Path.Combine(workspace.FullName, path2: "Counter.razor");
            string samplePath = Path.Combine(workspace.FullName, path2: "Sample.razor");

            // The shared framework comes from the SDK's own packs, so the local package is the only feed.
            string projectContent = $$"""
                <Project Sdk="Microsoft.NET.Sdk.Razor">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
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
            await File.WriteAllTextAsync(counterPath, Counter);
            await ConsumerBuild.RunDevelopmentKit(workspace.FullName, ["restore", projectPath, "--packages", Path.Combine(workspace.FullName, path2: "packages")]);
            string[] buildArguments = ["build", projectPath, "--no-restore", "--nologo", "--verbosity", "quiet"];

            await ConsumerBuild.RunDevelopmentKit(workspace.FullName, buildArguments, forbiddenOutput: ["NetAgents automatic formatting failed"]);
            Assert.Equal(Counter, await File.ReadAllTextAsync(counterPath));

            await File.WriteAllTextAsync(samplePath, Sample);

            await ConsumerBuild.RunDevelopmentKit(
                workspace.FullName,
                buildArguments,
                expectedDiagnosticIdentifier: "NETAGENTS0015",
                ["Sample.razor(9,9): error NETAGENTS0015", "Sample.razor(15,9): error NETAGENTS0004"],
                ["Counter.razor(", "NetAgents automatic formatting failed"]
            );

            Assert.Equal(Sample, await File.ReadAllTextAsync(samplePath));
            Assert.Equal(Counter, await File.ReadAllTextAsync(counterPath));
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }
}
