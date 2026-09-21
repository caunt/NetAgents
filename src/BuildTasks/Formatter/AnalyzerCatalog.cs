using System.Collections.Immutable;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.BuildTasks;

internal static class AnalyzerCatalog
{
    // Instantiates the C# analyzers an assembly declares without consulting the compiler version it was
    // built against. Every analyzer that cannot be built, or cannot describe its diagnostics, is named; an
    // assembly no runtime can load at all is left to the caller, which already reports why.
    public static (bool Read, ImmutableArray<DiagnosticAnalyzer> Analyzers, string[] Unavailable) Host(string path, AnalyzerLoader loader)
    {
        List<DiagnosticAnalyzer> analyzers = [];
        List<string> unavailable = [];
        string file = Path.GetFileName(path);
        Type[] declared;

        try
        {
            declared = [.. CompositionParts.GetLoadableTypes(loader.LoadFromPath(path))];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The caller already reports why the compiler's own loader refused the whole assembly.
            return (false, [], []);
        }

        foreach (Type type in declared)
        {
            try
            {
                if (!IsCSharpAnalyzer(type))
                    continue;

                DiagnosticAnalyzer analyzer = Create(type);

                // An analyzer that cannot describe its rules faults on every compilation it joins.
                if (!analyzer.SupportedDiagnostics.IsEmpty)
                    analyzers.Add(analyzer);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                unavailable.Add($"NetAgents cannot create the {type.FullName} analyzer from {file}: {exception.Message}");
            }
        }

        return (true, [.. analyzers], [.. unavailable]);
    }

    // Roslyn refuses an analyzer assembly that references a newer compiler than the host and announces
    // it through AnalyzerFileReference.AnalyzerLoadFailed, which nobody listened to: every SDK
    // code-style analyzer disappeared from this workspace whenever the consumer's SDK shipped a newer
    // Roslyn than this package carries, and the build silently stopped applying IDE fixes it still
    // reported as errors afterwards. The referenced version is metadata rather than a proven
    // incompatibility, and AnalyzerLoader already binds an analyzer's compiler references to the hosted
    // copy, so a refused assembly's analyzers are constructed here instead of dropped.
    public static (AnalyzerReference[] References, string[] Hosted, string[] Unavailable) Load(string[] analyzerPaths, AnalyzerLoader loader)
    {
        List<AnalyzerReference> references = [];
        List<string> hosted = [];
        List<string> unavailable = [];

        foreach (string path in analyzerPaths)
        {
            (AnalyzerReference reference, string? announcement, string[] failures) = Load(path, loader);
            references.Add(reference);

            if (announcement is not null)
                hosted.Add(announcement);

            unavailable.AddRange(failures);
        }

        return ([.. references], [.. hosted], [.. unavailable]);
    }

    private static DiagnosticAnalyzer Create(Type type)
    {
        ConstructorInfo constructor = type.GetConstructor(Type.EmptyTypes)
            ?? throw new MissingMemberException(type.FullName, memberName: "the parameterless constructor an analyzer needs");

        // A compiled constructor call keeps the instance typed, which Activator.CreateInstance does not.
        return Expression.Lambda<Func<DiagnosticAnalyzer>>(Expression.New(constructor)).Compile()();
    }

    private static string Describe(string path, AnalyzerLoadFailureEventArgs failure)
    {
        string file = Path.GetFileName(path);

        return failure.TypeName is null
            ? $"NetAgents cannot load analyzers from {file}: {Explain(failure)}"
            : $"NetAgents cannot load the {failure.TypeName} analyzer from {file}: {Explain(failure)}";
    }

    // A refusal over the compiler version carries the version it wanted and no message at all.
    private static string Explain(AnalyzerLoadFailureEventArgs failure)
    {
        string reason = failure.ReferencedCompilerVersion is null
            ? failure.ErrorCode.ToString()
            : $"{failure.ErrorCode}, which wants Roslyn {failure.ReferencedCompilerVersion}";

        return string.IsNullOrEmpty(failure.Message) ? reason : $"{reason} ({failure.Message})";
    }

    private static bool IsCSharpAnalyzer(Type type)
    {
        // A generic definition has no constructor to call, and Roslyn's own loader never offers one.
        if (type.IsAbstract || type.ContainsGenericParameters || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            return false;

        DiagnosticAnalyzerAttribute? attribute = type.GetCustomAttribute<DiagnosticAnalyzerAttribute>(inherit: false);

        return attribute is not null && attribute.Languages.Contains(LanguageNames.CSharp, StringComparer.Ordinal);
    }

    private static (AnalyzerReference Reference, string? Hosted, string[] Unavailable) Load(string path, AnalyzerLoader loader)
    {
        List<AnalyzerLoadFailureEventArgs> failures = [];
        AnalyzerFileReference reference = new(path, loader);

        // An assembly that carries only code fixes holds no analyzers at all, which is not a failure.
        // The handler stays attached because the reference outlives this method, and the workspace's own
        // calls append to a list nothing reads again.
        reference.AnalyzerLoadFailed += (sender, failure) =>
        {
            if (failure.ErrorCode != AnalyzerLoadFailureEventArgs.FailureErrorCode.NoAnalyzers)
                failures.Add(failure);
        };

        ImmutableArray<DiagnosticAnalyzer> loaded = reference.GetAnalyzers(LanguageNames.CSharp);

        // A failure that names a type left the rest of the assembly usable; one that names none rejected
        // the assembly whole, and only then is the assembly worth hosting outside the compiler's loader.
        AnalyzerLoadFailureEventArgs[] rejections = [.. failures.Where(static failure => failure.TypeName is null)];

        if (rejections.Length == 0 || !loaded.IsEmpty)
            return (reference, null, [.. failures.Select(failure => Describe(path, failure))]);

        (bool read, ImmutableArray<DiagnosticAnalyzer> analyzers, string[] unavailable) = Host(path, loader);

        if (analyzers.IsEmpty)
        {
            // An assembly no runtime can read is enforcement lost; one that carries source generators and
            // no analyzer at all, as the Razor compiler does, costs this formatter nothing to skip.
            string[] refused = read ? [] : [.. rejections.Select(failure => Describe(path, failure))];

            return (reference, null, [.. refused, .. unavailable]);
        }

        string count = analyzers.Length.ToString(CultureInfo.InvariantCulture);
        string rules = analyzers.Length == 1 ? "analyzer" : "analyzers";
        string reasons = string.Join(separator: ", ", rejections.Select(Explain));

        return (
            new HostedAnalyzerReference(path, analyzers),
            $"NetAgents hosts {count} {rules} from {Path.GetFileName(path)} that this formatter's Roslyn refused: {reasons}",
            unavailable
        );
    }
}
