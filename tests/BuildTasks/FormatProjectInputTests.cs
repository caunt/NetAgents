using Microsoft.Build.Framework;

using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.BuildTasks;

/// <summary>
/// Covers how the formatting task reads the item lists its targets pass as text, one item per line.
/// </summary>
public sealed class FormatProjectInputTests
{
    /// <summary>
    /// Verifies a list the targets leave empty reads as no items, so a project without additional files or analyzers
    /// formats as before.
    /// </summary>
    [Fact]
    public void ReadsEmptyListsAsNoItems()
    {
        using FormatProject task = new();

        Assert.Empty(task.AdditionalItems);
        Assert.Empty(task.AnalyzerItems);
        Assert.Empty(task.BuildItems);
        Assert.Empty(task.ConfigurationItems);
    }

    /// <summary>
    /// Verifies each line becomes one item whose spec is the path, that tab-separated fields become the metadata the
    /// formatter reads, and that empty lines, Windows line endings, and a literal percent sign survive.
    /// </summary>
    [Fact]
    public void ReadsOneItemPerLine()
    {
        using FormatProject task = new()
        {
            SourceFiles = "/src/Program.cs\tShared/Program.cs\r\n\n/src/100%25 Done.cs\n",
            References = "/lib/Library.dll\tExternal\ttrue\n/lib/Plain.dll",
        };

        IReadOnlyList<ITaskItem> sources = task.SourceItems;
        IReadOnlyList<ITaskItem> references = task.ReferenceItems;

        // Sources carry the link a linked file shows in the project, and nothing else.
        Assert.Equal(expected: 2, sources.Count);
        Assert.Equal(expected: "/src/Program.cs", sources[index: 0].ItemSpec);
        Assert.Equal(expected: "Shared/Program.cs", sources[index: 0].GetMetadata(metadataName: "Link"));
        Assert.Equal(expected: "/src/100%25 Done.cs", sources[index: 1].ItemSpec);

        // References carry their aliases and whether interop types are embedded, empty when a line leaves them out.
        Assert.Equal(expected: "External", references[index: 0].GetMetadata(metadataName: "Aliases"));
        Assert.Equal(expected: "true", references[index: 0].GetMetadata(metadataName: "EmbedInteropTypes"));
        Assert.Equal(string.Empty, references[index: 1].GetMetadata(metadataName: "Aliases"));
    }
}
