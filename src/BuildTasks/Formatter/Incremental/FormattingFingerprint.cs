using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NetAgents.BuildTasks;

// File contents, ordered item identities, and metadata are inputs. Timestamps and Roslyn's randomly
// allocated DocumentIds are not: a same-length edit with a restored timestamp must still invalidate.
internal sealed record FormattingFingerprint(string Context, string Sources)
{
    public string Value => Context + ":" + Sources;

    public static async Task<FormattingFingerprint> Create(FormatProject inputs, IReadOnlyDictionary<string, byte[]> sources, CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Append(hash, value: "NetAgents formatting inputs v1");
        Append(hash, Path.GetFullPath(inputs.ProjectPath));
        Append(hash, inputs.AssemblyName);
        Append(hash, inputs.RootNamespace);
        Append(hash, inputs.LanguageVersion);
        Append(hash, inputs.DefineConstants);
        Append(hash, inputs.AllowUnsafe.ToString(CultureInfo.InvariantCulture));
        Append(hash, inputs.OutputType);
        Append(hash, RuntimeInformation.FrameworkDescription);
        Append(hash, RuntimeInformation.RuntimeIdentifier);
        Append(hash, CultureInfo.CurrentCulture.Name);
        Append(hash, CultureInfo.CurrentUICulture.Name);
        // The host is retained inside an MSBuild process. If its files are replaced on disk, a
        // result produced by the loaded engine must not become a hit for a newly loaded engine.
        Append(hash, typeof(FormatProject).Assembly.ManifestModule.ModuleVersionId.ToString());
        Append(hash, typeof(FormattingFingerprint).Assembly.ManifestModule.ModuleVersionId.ToString());
        Append(hash, typeof(Compilation).Assembly.ManifestModule.ModuleVersionId.ToString());
        Append(hash, typeof(Workspace).Assembly.ManifestModule.ModuleVersionId.ToString());
        Append(hash, typeof(CSharpCompilation).Assembly.ManifestModule.ModuleVersionId.ToString());

        await AppendFiles(hash, inputs.References, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        await AppendFiles(hash, inputs.AnalyzerFiles, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        await AppendFiles(hash, inputs.ConfigurationFiles, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        await AppendFiles(hash, inputs.AdditionalFiles, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        await AppendFiles(hash, inputs.BuildFiles, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        AppendItems(hash, inputs.BuildProperties);

        string packagedDirectory = Path.GetDirectoryName(typeof(FormatProject).Assembly.Location)
            ?? throw new InvalidOperationException(message: "The formatting task's directory is unavailable.");

        // Include both hosts and every sibling dependency the analyzer loader can resolve, not just
        // the analyzer entry points. Adding or removing an assembly also changes this inventory.
        IEnumerable<string> directories = new[] { packagedDirectory, inputs.SdkAssemblyDirectory, inputs.CodeFixDirectory }
            .Concat(inputs.AnalyzerFiles.Select(static item => Path.GetDirectoryName(item.GetMetadata(metadataName: "FullPath")) ?? string.Empty));

        foreach (string directory in directories.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            Append(hash, directory);

            string[] files = Directory.Exists(directory)
                ? [.. Directory.EnumerateFiles(directory, searchPattern: "*.dll", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(directory, searchPattern: "*.deps.json", SearchOption.AllDirectories)).Order(StringComparer.Ordinal)]
                : [];

            Append(hash, files.Length.ToString(CultureInfo.InvariantCulture));

            foreach (string file in files)
                await AppendFile(hash, file, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        return new(Convert.ToHexString(hash.GetHashAndReset()), HashSources(inputs.SourceFiles, sources));
    }

    public static string HashSources(ITaskItem[] items, IReadOnlyDictionary<string, byte[]> sources)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        AppendItems(hash, items);

        foreach (ITaskItem item in items)
        {
            string path = item.GetMetadata(metadataName: "FullPath");
            Append(hash, path);
            hash.AppendData(SHA256.HashData(sources[path]));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static async Task<Dictionary<string, byte[]>> ReadSources(ITaskItem[] items, CancellationToken cancellationToken)
    {
        Dictionary<string, byte[]> sources = new(StringComparer.Ordinal);

        foreach (ITaskItem item in items)
        {
            string path = item.GetMetadata(metadataName: "FullPath");
            sources[path] = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        return sources;
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static async Task AppendFile(IncrementalHash hash, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Append(hash, Path.GetFullPath(path));

        if (!File.Exists(path))
        {
            Append(hash, value: "missing");

            return;
        }

        Append(hash, value: "present");

        FileStream stream = File.OpenRead(path);

        await using (stream.ConfigureAwait(continueOnCapturedContext: false))
        {
            byte[] checksum = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            hash.AppendData(checksum);
        }
    }

    private static async Task AppendFiles(IncrementalHash hash, ITaskItem[] items, CancellationToken cancellationToken)
    {
        AppendItems(hash, items);

        foreach (ITaskItem item in items)
            await AppendFile(hash, item.GetMetadata(metadataName: "FullPath"), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private static void AppendItems(IncrementalHash hash, ITaskItem[] items)
    {
        Append(hash, items.Length.ToString(CultureInfo.InvariantCulture));

        foreach (ITaskItem item in items)
        {
            Append(hash, item.ItemSpec);
            string[] names = [.. item.CloneCustomMetadata().Keys.Cast<string>().Order(StringComparer.Ordinal)];
            Append(hash, names.Length.ToString(CultureInfo.InvariantCulture));

            foreach (string name in names)
            {
                Append(hash, name);
                Append(hash, item.GetMetadata(name));
            }
        }
    }
}
