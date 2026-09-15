using System.Diagnostics.CodeAnalysis;

using Microsoft.Build.Framework;

using Nito.AsyncEx;

namespace NetAgents.BuildTasks;

/// <summary>
/// Applies available Roslyn formatting and code fixes directly inside the current MSBuild process.
/// </summary>
[SuppressMessage("Performance", "CA1819", Justification = "MSBuild requires item-list task parameters to expose ITaskItem arrays.")]
public sealed class FormatProject : Microsoft.Build.Utilities.Task, ICancelableTask, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Gets or sets the absolute consumer project path.</summary>
    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Gets or sets whether unsafe code is enabled.</summary>
    public bool AllowUnsafe { get; set; }

    /// <summary>Gets or sets the compilation assembly name.</summary>
    [Required]
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>Gets or sets the root namespace used by namespace style fixes.</summary>
    public string RootNamespace { get; set; } = string.Empty;

    /// <summary>Gets or sets the C# language version.</summary>
    public string LanguageVersion { get; set; } = "default";

    /// <summary>Gets or sets the active conditional compilation symbols.</summary>
    public string DefineConstants { get; set; } = string.Empty;

    /// <summary>Gets or sets the compilation output kind.</summary>
    public string OutputType { get; set; } = "Library";

    /// <summary>Gets or sets the source files included in this compilation.</summary>
    [Required]
    public ITaskItem[] SourceFiles { get; set; } = [];

    /// <summary>Gets or sets the resolved compilation references.</summary>
    public ITaskItem[] References { get; set; } = [];

    /// <summary>Gets or sets the configured analyzer and code-fix assemblies.</summary>
    public ITaskItem[] AnalyzerFiles { get; set; } = [];

    /// <summary>Gets or sets the analyzer additional files.</summary>
    public ITaskItem[] AdditionalFiles { get; set; } = [];

    /// <summary>Gets or sets the effective editorconfig and globalconfig files.</summary>
    public ITaskItem[] ConfigurationFiles { get; set; } = [];

    /// <summary>Gets or sets the SDK's C# code-style provider directory.</summary>
    public string CodeFixDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the SDK directory containing code-style runtime dependencies.</summary>
    public string SdkAssemblyDirectory { get; set; } = string.Empty;

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
        using (_cancellation)
        {
            try
            {
                AsyncContext.Run(() => ProjectFormatter.Format(this, _cancellation.Token));

                return true;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException)
            {
                Log.LogError("NetAgents automatic formatting failed: " + exception.Message);

                return false;
            }
        }
    }

}
