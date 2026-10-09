using System.Text;

namespace NetAgents.BuildTasks;

internal sealed class FormattingCache(string path)
{
    public void Invalidate()
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unavailable cache only costs work. It must never disable formatting or fail a build.
            return;
        }
    }

    public bool Matches(FormattingFingerprint fingerprint)
    {
        try
        {
            return string.Equals(File.ReadAllText(path), fingerprint.Value, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task Store(FormattingFingerprint fingerprint, CancellationToken cancellationToken)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString(format: "N") + ".tmp";

        try
        {
            DirectoryInfo directory = new(
                Path.GetDirectoryName(Path.GetFullPath(path))
                ?? throw new InvalidOperationException(message: "The formatting cache directory is unavailable.")
            );

            directory.Create();
            await File.WriteAllTextAsync(temporaryPath, fingerprint.Value, Encoding.UTF8, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A partial write cannot look like a completed run; the next build simply formats again.
            return;
        }
        finally
        {
            new FormattingCache(temporaryPath).Invalidate();
        }
    }
}
