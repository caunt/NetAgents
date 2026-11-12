using System.Diagnostics.CodeAnalysis;

using Microsoft.Build.Framework;

using UtilitiesTask = Microsoft.Build.Utilities.Task;

namespace NetAgents.BuildTasks;

/// <summary>
/// Applies available Roslyn formatting and code fixes directly inside the current MSBuild process.
/// </summary>
[SuppressMessage("Performance", "CA1819", Justification = "MSBuild requires item-list task parameters to expose ITaskItem arrays.")]
public sealed class FormatProject : UtilitiesTask, ICancelableTask, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Gets or sets the absolute consumer project path.</summary>
    /// <value>The full path of the project file being built, from <c>MSBuildProjectFullPath</c>.</value>
    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Gets or sets whether unsafe code is enabled.</summary>
    /// <value><see langword="true"/> if the project sets <c>AllowUnsafeBlocks</c>; otherwise, <see langword="false"/>.</value>
    public bool AllowUnsafe { get; set; }

    /// <summary>Gets or sets the compilation assembly name.</summary>
    /// <value>The project's <c>AssemblyName</c>, which names the compilation the fixes analyze.</value>
    [Required]
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>Gets or sets the root namespace used by namespace style fixes.</summary>
    /// <value>The project's <c>RootNamespace</c>, or an empty string when the project sets none.</value>
    public string RootNamespace { get; set; } = string.Empty;

    /// <summary>Gets or sets the C# language version.</summary>
    /// <value>The project's <c>LangVersion</c>, such as <c>latest</c> or <c>14</c>; <c>default</c> when unset.</value>
    public string LanguageVersion { get; set; } = "default";

    /// <summary>Gets or sets the active conditional compilation symbols.</summary>
    /// <value>The project's <c>DefineConstants</c>, separated by semicolons or commas.</value>
    public string DefineConstants { get; set; } = string.Empty;

    /// <summary>Gets or sets the compilation output kind.</summary>
    /// <value>The project's <c>OutputType</c>, such as <c>Library</c> or <c>Exe</c>; <c>Library</c> when unset.</value>
    public string OutputType { get; set; } = "Library";

    /// <summary>Gets or sets the source files included in this compilation.</summary>
    /// <value>The project's <c>Compile</c> items, which are the files the fixes may rewrite.</value>
    [Required]
    public ITaskItem[] SourceFiles { get; set; } = [];

    /// <summary>Gets or sets the resolved compilation references.</summary>
    /// <value>The <c>ReferencePathWithRefAssemblies</c> items the compiler resolved for this build.</value>
    public ITaskItem[] References { get; set; } = [];

    /// <summary>Gets or sets the configured analyzer and code-fix assemblies.</summary>
    /// <value>The analyzer and code-fix assemblies the package selected for formatting.</value>
    public ITaskItem[] AnalyzerFiles { get; set; } = [];

    /// <summary>Gets or sets the analyzer additional files.</summary>
    /// <value>The project's <c>AdditionalFiles</c> items, passed to analyzers as they are to the compiler.</value>
    public ITaskItem[] AdditionalFiles { get; set; } = [];

    /// <summary>Gets or sets the effective editorconfig and globalconfig files.</summary>
    /// <value>The <c>EditorConfigFiles</c> items, including the shared NetAgents global configuration.</value>
    public ITaskItem[] ConfigurationFiles { get; set; } = [];

    /// <summary>Gets or sets the SDK's C# code-style provider directory.</summary>
    /// <value>The <c>codestyle/cs</c> directory of the building SDK, where its C# code fixes ship.</value>
    public string CodeFixDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the SDK directory containing code-style runtime dependencies.</summary>
    /// <value>The SDK's <c>dotnet-format</c> directory, which holds the Roslyn those code fixes run on.</value>
    public string SdkAssemblyDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the intermediate file recording successfully formatted inputs.</summary>
    /// <value>A path under the intermediate output directory, unique to the project file.</value>
    [Required]
    public string CacheFile { get; set; } = string.Empty;

    /// <summary>Gets or sets the project, imports, and resolved package graph used by this build.</summary>
    /// <value>The files whose changes invalidate the record of completed formatting.</value>
    public ITaskItem[] BuildFiles { get; set; } = [];

    /// <summary>Gets or sets the evaluated compiler and build settings.</summary>
    /// <value>Name and value pairs of the properties that affect what the formatter sees.</value>
    public ITaskItem[] BuildProperties { get; set; } = [];

    /// <inheritdoc />
    public void Cancel()
    {
        _cancellation.Cancel();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cancellation.Dispose();
    }

    /// <inheritdoc />
    [SuppressMessage("Design",
        "CA1031",
        Justification = "Every formatting failure must reach the log as a build error; an unhandled exception aborts the build with MSB4018 instead.")]
    public override bool Execute()
    {
        // Dispose() owns the source: MSBuild can still call Cancel() while this method unwinds.
        try
        {
            Action<FormatProject, CancellationToken> format = FormattingHost.Resolve(SdkAssemblyDirectory);
            format(this, _cancellation.Token);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            Log.LogError("NetAgents automatic formatting failed: " + exception.Message);

            return false;
        }
        catch (Exception exception)
        {
            // MSB4018 would hide the formatter behind an MSBuild task crash, so the stack is logged instead.
            Log.LogError("NetAgents automatic formatting failed unexpectedly: " + exception.ToString());

            return false;
        }
    }
}
