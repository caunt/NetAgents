using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.BuildTasks;

// Carries analyzers this formatter constructed itself, after Roslyn refused the assembly they came
// from. AnalyzerFileReference would keep refusing it, so the project holds the instances instead.
internal sealed class HostedAnalyzerReference(string path, ImmutableArray<DiagnosticAnalyzer> analyzers) : AnalyzerReference
{
    public override string Display => Path.GetFileName(path);

    public override string FullPath => path;

    public override object Id => path;

    public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language)
    {
        return string.Equals(language, LanguageNames.CSharp, StringComparison.Ordinal) ? analyzers : [];
    }

    public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages()
    {
        return analyzers;
    }
}
