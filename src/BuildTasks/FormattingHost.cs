using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace NetAgents.BuildTasks;

// The consumer's SDK hands this task the analyzers it ships, compiled against the Roslyn that SDK
// carries, and Roslyn refuses an analyzer whose compiler is newer than the one hosting it. Loading the
// formatting engine on the SDK's own Roslyn removes that mismatch for every SDK, current and future;
// the copy inside this package stays behind it for a layout that ships none. The engine is compiled
// against the packaged version, which is the compatible direction, because Roslyn only adds API.
internal sealed class FormattingHost : AssemblyLoadContext
{
    // Roslyn costs tens of megabytes to load and a solution build formats one project after another,
    // so each SDK's engine is built once per MSBuild process and kept.
    private static readonly ConcurrentDictionary<string, Action<FormatProject, CancellationToken>> Engines = new(StringComparer.Ordinal);

    private readonly string[] _directories;

    private readonly AssemblyLoadContext _taskContext;

    private FormattingHost(string packagedDirectory, string sdkAssemblyDirectory) : base(name: "NetAgents formatting")
    {
        _taskContext = GetLoadContext(typeof(FormattingHost).Assembly)
            ?? throw new InvalidOperationException(message: "The formatting task's assembly context is unavailable.");

        _directories = Directory.Exists(sdkAssemblyDirectory) ? [sdkAssemblyDirectory, packagedDirectory] : [packagedDirectory];
    }

    public static Action<FormatProject, CancellationToken> Resolve(string sdkAssemblyDirectory)
    {
        return Engines.GetOrAdd(sdkAssemblyDirectory, Create);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string name = assemblyName.Name ?? string.Empty;

        // The inputs and the build log cross this boundary, so both sides need the same task and
        // MSBuild types rather than a second copy of them.
        bool shared = string.Equals(name, b: "NetAgents.BuildTasks", StringComparison.Ordinal)
            || name.StartsWith(value: "Microsoft.Build", StringComparison.Ordinal);

        if (shared)
            return _taskContext.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal));

        foreach (string directory in _directories)
        {
            string path = Path.Combine(directory, name + ".dll");

            if (File.Exists(path))
                return LoadFromAssemblyPath(path);
        }

        // Everything the runtime itself owns resolves the way it always does.
        return null;
    }

    private static Action<FormatProject, CancellationToken> Create(string sdkAssemblyDirectory)
    {
        string packagedDirectory = Path.GetDirectoryName(typeof(FormattingHost).Assembly.Location)
            ?? throw new InvalidOperationException(message: "The formatting task's directory is unavailable.");

        FormattingHost host = new(packagedDirectory, sdkAssemblyDirectory);
        Assembly engine = host.LoadFromAssemblyPath(Path.Combine(packagedDirectory, path2: "NetAgents.Formatter.dll"));

        MethodInfo? entryPoint = engine.GetType(name: "NetAgents.BuildTasks.FormattingEngine")
            ?.GetMethod(name: "Run", BindingFlags.Public | BindingFlags.Static);

        return entryPoint is null
            ? throw new InvalidOperationException(message: "The formatting engine in this package has no entry point.")
            : entryPoint.CreateDelegate<Action<FormatProject, CancellationToken>>();
    }
}
