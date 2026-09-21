using Nito.AsyncEx;

namespace NetAgents.BuildTasks;

/// <summary>
/// Formats a consumer project on the Roslyn this assembly was loaded with.
/// </summary>
public static class FormattingEngine
{
    /// <summary>Formats every source file the task was given and waits for the result.</summary>
    /// <param name="inputs">The task holding the consumer project's compilation inputs.</param>
    /// <param name="cancellationToken">The token MSBuild cancels the build with.</param>
    public static void Run(FormatProject inputs, CancellationToken cancellationToken)
    {
        // The formatting work owns its own single-threaded context, so MSBuild's thread is not reentered.
        AsyncContext.Run(() => ProjectFormatter.Format(inputs, cancellationToken));
    }
}
