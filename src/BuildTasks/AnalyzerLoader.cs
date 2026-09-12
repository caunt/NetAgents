using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;

namespace NetAgents.BuildTasks;

internal sealed class AnalyzerLoader() : AssemblyLoadContext(isCollectible: true), IAnalyzerAssemblyLoader, IDisposable
{
    private readonly AssemblyLoadContext _sharedContext = GetLoadContext(typeof(AnalyzerLoader).Assembly)
        ?? throw new InvalidOperationException(message: "The formatting task's assembly context is unavailable.");

    private readonly List<string> _directories = [];

    public void AddDependencyLocation(string fullPath)
    {
        string? directory = Path.GetDirectoryName(fullPath);

        if (directory is not null && !_directories.Contains(directory, StringComparer.Ordinal))
            _directories.Add(directory);
    }

    public void Dispose()
    {
        Unload();
    }

    public Assembly LoadFromPath(string fullPath)
    {
        AddDependencyLocation(fullPath);

        string? name = AssemblyName.GetAssemblyName(fullPath).Name;
        Assembly? existing = Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == name);

        return existing ?? LoadFromAssemblyPath(fullPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string name = assemblyName.Name ?? string.Empty;

        bool shared = name is "Microsoft.CodeAnalysis" or "Microsoft.CodeAnalysis.CSharp"
            or "Microsoft.CodeAnalysis.Workspaces" or "Microsoft.CodeAnalysis.CSharp.Workspaces"
            || name.StartsWith(value: "System.", StringComparison.Ordinal);

        if (shared)
        {
            Assembly? assembly = _sharedContext.Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == name);

            if (assembly is not null)
                return assembly;
        }

        foreach (string directory in _directories)
        {
            string path = Path.Combine(directory, name + ".dll");

            if (File.Exists(path))
                return LoadFromAssemblyPath(path);
        }

        return null;
    }
}
