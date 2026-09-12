using System.Security.Cryptography;
using System.Text;

namespace NetAgents.BuildTasks;

internal sealed class FormattingLock(List<FileStream> streams) : IAsyncDisposable
{
    public static async Task<FormattingLock> Acquire(string[] sourcePaths, CancellationToken cancellationToken)
    {
        List<FileStream> acquired = [];

        try
        {
            string[] directories = [.. sourcePaths.Select(
                static path => Normalize(Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new InvalidOperationException(message: "A source directory is missing."))
            )
                .Distinct(StringComparer.Ordinal).OrderBy(static path => path, StringComparer.Ordinal)];

            // Global ordering also serializes projects that compile the same linked source files.
            foreach (string directory in directories)
                acquired.Add(await AcquireDirectory(directory, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));

            return new FormattingLock(acquired);
        }
        catch
        {
            foreach (FileStream stream in acquired)
                await stream.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);

            throw;
        }
    }

    private static async Task<FileStream> AcquireDirectory(string directory, CancellationToken cancellationToken)
    {
        string identifier = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory)));
        string path = Path.Combine(Path.GetTempPath(), "netagents-format-" + identifier + ".lock");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(milliseconds: 100), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private static string Normalize(string path)
    {
        return OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        foreach (FileStream stream in streams)
            await stream.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
    }
}
