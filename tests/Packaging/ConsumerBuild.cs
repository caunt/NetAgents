using System.Diagnostics;
using System.Reflection;

namespace NetAgents.Analyzers.Tests.Packaging;

/// <summary>
/// Packs the analyzer and runs the development kit against a temporary consumer workspace.
/// </summary>
internal static class ConsumerBuild
{
    internal static async Task<string> PackAnalyzer(string workspacePath)
    {
        DirectoryInfo? repositoryDirectory = new(AppContext.BaseDirectory);

        while (repositoryDirectory is not null && !File.Exists(Path.Combine(paths: [repositoryDirectory.FullName, "NetAgents.slnx"])))
            repositoryDirectory = repositoryDirectory.Parent;

        Assert.NotNull(repositoryDirectory);

        string configuration = typeof(ConsumerBuild).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            ?? throw new InvalidOperationException(message: "The test build configuration is missing.");

        string packageDirectory = Path.Combine(paths: [workspacePath, "artifacts"]);
        await RunDevelopmentKit(
            repositoryDirectory.FullName,
                    [
            "pack", "src/NetAgents.Analyzers.csproj", "--configuration", configuration,
            "--no-build", "--no-restore", "--output", packageDirectory,
        ]
        ).ConfigureAwait(continueOnCapturedContext: false);

        return Assert.Single(Directory.EnumerateFiles(packageDirectory, searchPattern: "*.nupkg"));
    }

    internal static async Task RunDevelopmentKit(
        string workingDirectory,
        string[] arguments,
        string? expectedDiagnosticIdentifier = null,
        string[]? expectedOutput = null,
        string[]? forbiddenOutput = null,
        bool withoutShell = false
    )
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

        // Reused MSBuild workers retain redirected pipes after the command exits.
        process.StartInfo.Environment[key: "MSBUILDDISABLENODEREUSE"] = "1";

        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        if (withoutShell)
        {
            process.StartInfo.Environment[key: "PATH"] = string.Empty;
            process.StartInfo.Environment[key: "SHELL"] = "/no-shell";
            process.StartInfo.Environment[key: "COMSPEC"] = "/no-shell";
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
        Assert.DoesNotContain(expectedSubstring: "AD0001", output, StringComparison.Ordinal);

        // A formatting fault must reach the log as a build error instead of aborting the task.
        Assert.DoesNotContain(expectedSubstring: "MSB4018", output, StringComparison.Ordinal);

        foreach (string expected in expectedOutput ?? [])
            Assert.Contains(expected, output, StringComparison.Ordinal);

        foreach (string forbidden in forbiddenOutput ?? [])
            Assert.DoesNotContain(forbidden, output, StringComparison.Ordinal);

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
