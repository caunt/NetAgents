using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Incremental;

/// <summary>Checks durable formatting completion and recovery from unusable cache files.</summary>
public sealed class FormattingCacheTests
{
    /// <summary>A cancelled write leaves neither a completion record nor a temporary file.</summary>
    [Fact]
    public async Task DoesNotPublishCancelledFormatting()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory(prefix: "netagents-cancelled-cache-");

        try
        {
            string path = Path.Combine(directory.FullName, path2: "formatting.cache");
            FormattingCache cache = new(path);
            FormattingFingerprint fingerprint = new(Context: "context", Sources: "sources");

            using CancellationTokenSource cancellation = new();

            await cancellation.CancelAsync();
            OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.Store(fingerprint, cancellation.Token));
            Assert.Equal(cancellation.Token, failure.CancellationToken);
            Assert.False(cache.Matches(fingerprint));
            Assert.Empty(directory.EnumerateFiles());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>Only a fully written record of the same complete inputs can be reused.</summary>
    [Fact]
    public async Task PersistsCompletionAndRejectsChangedOrCorruptInputs()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory(prefix: "netagents-cache-");

        try
        {
            string path = Path.Combine(directory.FullName, path2: "obj/formatting.cache");
            FormattingCache cache = new(path);
            FormattingFingerprint fingerprint = new(Context: "context", Sources: "sources");

            Assert.False(cache.Matches(fingerprint));
            await cache.Store(fingerprint, CancellationToken.None);
            Assert.True(new FormattingCache(path).Matches(fingerprint));
            Assert.False(cache.Matches(fingerprint with { Context = "changed" }));
            Assert.False(cache.Matches(fingerprint with { Sources = "changed" }));

            await File.WriteAllTextAsync(path, contents: "partial record");
            Assert.False(cache.Matches(fingerprint));
            await cache.Store(fingerprint, CancellationToken.None);
            cache.Invalidate();
            Assert.False(cache.Matches(fingerprint));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
