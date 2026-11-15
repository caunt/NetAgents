using Microsoft.Build.Framework;

using UtilitiesTask = Microsoft.Build.Utilities.Task;

namespace NetAgents.BuildTasks;

/// <summary>
/// Applies available Roslyn formatting and code fixes directly inside the current MSBuild process.
/// </summary>
public sealed class FormatProject : UtilitiesTask, ICancelableTask, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Gets the analyzer additional files read from <see cref="AdditionalFiles"/>.</summary>
    /// <value>One item per line of <see cref="AdditionalFiles"/>, its item spec the file's full path.</value>
    public IReadOnlyList<ITaskItem> AdditionalItems => TaskItemLines.Parse(AdditionalFiles);

    /// <summary>Gets the analyzer and code-fix assemblies read from <see cref="AnalyzerFiles"/>.</summary>
    /// <value>One item per line of <see cref="AnalyzerFiles"/>, its item spec the assembly's full path.</value>
    public IReadOnlyList<ITaskItem> AnalyzerItems => TaskItemLines.Parse(AnalyzerFiles);

    /// <summary>Gets the project, import, and package graph files read from <see cref="BuildFiles"/>.</summary>
    /// <value>One item per line of <see cref="BuildFiles"/>, its item spec the file's full path.</value>
    public IReadOnlyList<ITaskItem> BuildItems => TaskItemLines.Parse(BuildFiles);

    /// <summary>Gets the editorconfig and globalconfig files read from <see cref="ConfigurationFiles"/>.</summary>
    /// <value>One item per line of <see cref="ConfigurationFiles"/>, its item spec the file's full path.</value>
    public IReadOnlyList<ITaskItem> ConfigurationItems => TaskItemLines.Parse(ConfigurationFiles);

    /// <summary>Gets the compilation references read from <see cref="References"/>.</summary>
    /// <value>One item per line of <see cref="References"/>, with its <c>Aliases</c> and <c>EmbedInteropTypes</c> metadata.</value>
    public IReadOnlyList<ITaskItem> ReferenceItems => TaskItemLines.Parse(References, metadataNames: ["Aliases", "EmbedInteropTypes"]);

    /// <summary>Gets the source files read from <see cref="SourceFiles"/>.</summary>
    /// <value>One item per line of <see cref="SourceFiles"/>, with its <c>Link</c> metadata where the line has one.</value>
    public IReadOnlyList<ITaskItem> SourceItems => TaskItemLines.Parse(SourceFiles, metadataNames: ["Link"]);

    /// <summary>Gets or sets the analyzer additional files, one full path per line.</summary>
    /// <value>The project's <c>AdditionalFiles</c> items, passed to analyzers as they are to the compiler.</value>
    public string AdditionalFiles { get; set; } = string.Empty;

    /// <summary>Gets or sets whether unsafe code is enabled.</summary>
    /// <value><see langword="true"/> if the project sets <c>AllowUnsafeBlocks</c>; otherwise, <see langword="false"/>.</value>
    public bool AllowUnsafe { get; set; }

    /// <summary>Gets or sets the configured analyzer and code-fix assemblies, one full path per line.</summary>
    /// <value>The analyzer and code-fix assemblies the package selected for formatting.</value>
    public string AnalyzerFiles { get; set; } = string.Empty;

    /// <summary>Gets or sets the compilation assembly name.</summary>
    /// <value>The project's <c>AssemblyName</c>, which names the compilation the fixes analyze.</value>
    [Required]
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>Gets or sets the project, imports, and resolved package graph used by this build, one full path per line.</summary>
    /// <value>The files whose changes invalidate the record of completed formatting.</value>
    public string BuildFiles { get; set; } = string.Empty;

    /// <summary>Gets or sets the evaluated compiler and build settings.</summary>
    /// <value>
    /// One <c>Name=Value</c> line for each property that affects what the formatter sees. The text is compared as a
    /// whole, so any change to a setting invalidates the record of completed formatting.
    /// </value>
    public string BuildProperties { get; set; } = string.Empty;

    /// <summary>Gets or sets the intermediate file recording successfully formatted inputs.</summary>
    /// <value>A path under the intermediate output directory, unique to the project file.</value>
    [Required]
    public string CacheFile { get; set; } = string.Empty;

    /// <summary>Gets or sets the SDK's C# code-style provider directory.</summary>
    /// <value>The <c>codestyle/cs</c> directory of the building SDK, where its C# code fixes ship.</value>
    public string CodeFixDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the effective editorconfig and globalconfig files, one full path per line.</summary>
    /// <value>The <c>EditorConfigFiles</c> items, including the shared NetAgents global configuration.</value>
    public string ConfigurationFiles { get; set; } = string.Empty;

    /// <summary>Gets or sets the active conditional compilation symbols.</summary>
    /// <value>The project's <c>DefineConstants</c>, separated by semicolons or commas.</value>
    public string DefineConstants { get; set; } = string.Empty;

    /// <summary>Gets or sets the C# language version.</summary>
    /// <value>The project's <c>LangVersion</c>, such as <c>latest</c> or <c>14</c>; <c>default</c> when unset.</value>
    public string LanguageVersion { get; set; } = "default";

    /// <summary>Gets or sets the compilation output kind.</summary>
    /// <value>The project's <c>OutputType</c>, such as <c>Library</c> or <c>Exe</c>; <c>Library</c> when unset.</value>
    public string OutputType { get; set; } = "Library";

    /// <summary>Gets or sets the absolute consumer project path.</summary>
    /// <value>The full path of the project file being built, from <c>MSBuildProjectFullPath</c>.</value>
    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the resolved compilation references, one per line.</summary>
    /// <value>
    /// The <c>ReferencePathWithRefAssemblies</c> items the compiler resolved for this build: each line a reference's
    /// full path, then a tab and its <c>Aliases</c>, then a tab and its <c>EmbedInteropTypes</c>.
    /// </value>
    public string References { get; set; } = string.Empty;

    /// <summary>Gets or sets the root namespace used by namespace style fixes.</summary>
    /// <value>The project's <c>RootNamespace</c>, or an empty string when the project sets none.</value>
    public string RootNamespace { get; set; } = string.Empty;

    /// <summary>Gets or sets the SDK directory containing code-style runtime dependencies.</summary>
    /// <value>The SDK's <c>dotnet-format</c> directory, which holds the Roslyn those code fixes run on.</value>
    public string SdkAssemblyDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the source files included in this compilation, one per line.</summary>
    /// <value>
    /// The project's <c>Compile</c> items, which are the files the fixes may rewrite: each line a file's full path,
    /// then a tab and its <c>Link</c> when the project links the file from elsewhere.
    /// </value>
    [Required]
    public string SourceFiles { get; set; } = string.Empty;

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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // MSB4018 would hide the formatter behind an MSBuild task crash, so the stack is logged instead. Only running
            // out of memory is left to crash the build, since logging cannot be trusted to work then.
            Log.LogError("NetAgents automatic formatting failed unexpectedly: " + exception.ToString());

            return false;
        }
    }
}
