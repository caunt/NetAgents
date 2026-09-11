using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.SourceFiles;

/// <summary>
/// Limits each directory to sixteen authored C# files in a compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectoryFileCountAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0018";

    private const int MaximumFileCount = 16;

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Directories must have a focused scope",
        messageFormat: "Directory '{0}' contains {1} authored C# files (maximum {2}); organize these files into subdirectories with narrower responsibilities",
        category: "Structure",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable, WellKnownDiagnosticTags.CompilationEnd]
    );

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterCompilationStartAction(
            static compilationContext =>
        {
            StringComparer pathComparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            ConcurrentDictionary<string, SyntaxTree> files = new(pathComparer);
            compilationContext.RegisterSyntaxTreeAction(treeContext => CollectFile(treeContext, files));
            compilationContext.RegisterCompilationEndAction(endContext => AnalyzeDirectories(endContext, files, pathComparer));
        }
        );
    }

    private static void CollectFile(SyntaxTreeAnalysisContext context, ConcurrentDictionary<string, SyntaxTree> files)
    {
        if (context.IsGeneratedCode || !context.Tree.FilePath.EndsWith(value: ".cs", StringComparison.OrdinalIgnoreCase))
            return;

        files[context.Tree.FilePath.Replace(oldChar: '\\', newChar: '/')] = context.Tree;
    }

    private static void AnalyzeDirectories(CompilationAnalysisContext context, ConcurrentDictionary<string, SyntaxTree> files, StringComparer pathComparer)
    {
        foreach (IGrouping<string, KeyValuePair<string, SyntaxTree>> directory in files.GroupBy(static file => Path.GetDirectoryName(file.Key) is { Length: > 0 } name ? name : ".", pathComparer))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int count = directory.Count();

            if (count <= MaximumFileCount)
                continue;

            SyntaxTree firstFile = directory.OrderBy(static file => file.Key, pathComparer).First().Value;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    Location.Create(firstFile, new TextSpan(start: 0, length: 0)),
                    directory.Key,
                    count.ToString(CultureInfo.InvariantCulture),
                    MaximumFileCount.ToString(CultureInfo.InvariantCulture)
                )
            );
        }
    }
}
