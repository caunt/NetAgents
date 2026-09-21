using System.Collections.Immutable;
using System.Composition.Hosting;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.BuildTasks;

internal static class ProjectFormatter
{
    public static async Task Format(FormatProject inputs, CancellationToken cancellationToken)
    {
        FormattingLock projectLock = await FormattingLock.Acquire([.. inputs.SourceFiles.Select(GetPath)], cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        await using (projectLock.ConfigureAwait(continueOnCapturedContext: false))
            await FormatCore(inputs, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private static PortableExecutableReference CreateReference(ITaskItem item)
    {
        string aliases = item.GetMetadata(metadataName: "Aliases");
        ImmutableArray<string> referenceAliases = string.IsNullOrEmpty(aliases) ? [] : [.. aliases.Split(separator: ',')];

        MetadataReferenceProperties properties = new(
            aliases: referenceAliases,
            embedInteropTypes: string.Equals(item.GetMetadata(metadataName: "EmbedInteropTypes"), b: "true", StringComparison.OrdinalIgnoreCase)
        );

        return MetadataReference.CreateFromFile(GetPath(item), properties);
    }

    private static async Task FormatCore(FormatProject inputs, CancellationToken cancellationToken)
    {
        using AnalyzerLoader loader = new();

        loader.AddDependencyLocation(Path.Combine(inputs.SdkAssemblyDirectory, path2: "dependencies.dll"));

        string[] analyzerPaths = [.. inputs.AnalyzerFiles.Select(GetPath).Where(File.Exists).Distinct(StringComparer.Ordinal)];

        foreach (string path in analyzerPaths)
            loader.AddDependencyLocation(path);

        string[] styleFixes = Directory.Exists(inputs.CodeFixDirectory)
            ? Directory.GetFiles(inputs.CodeFixDirectory, searchPattern: "*Fixes.dll")
            : [];

        string[] featurePaths = [Path.Combine(inputs.SdkAssemblyDirectory, path2: "Microsoft.CodeAnalysis.Features.dll"),
            Path.Combine(inputs.SdkAssemblyDirectory, path2: "Microsoft.CodeAnalysis.CSharp.Features.dll")];

        // The SDK's feature assemblies belong to the Roslyn already hosting this engine. Loading them
        // beside it instead of inside the analyzer loader keeps one copy of each: a second copy composes
        // a second set of MEF exports, and Roslyn's own service lookups reject the ambiguity.
        AssemblyLoadContext host = AssemblyLoadContext.GetLoadContext(typeof(ProjectFormatter).Assembly)
            ?? throw new InvalidOperationException(message: "The formatting engine's assembly context is unavailable.");

        Assembly[] assemblies = [.. MefHostServices.DefaultAssemblies,
            typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions).Assembly,
            .. analyzerPaths.Concat(styleFixes).Select(loader.LoadFromPath),
            .. featurePaths.Where(File.Exists).Select(host.LoadFromAssemblyPath)];

        (Type[] parts, string[] skipped) = CompositionParts.Select(assemblies.Distinct());

        foreach (string part in skipped)
            inputs.Log.LogMessage(MessageImportance.Normal, $"NetAgents skips the {part} export because this build cannot load every type it declares");

        using CompositionHost composition = new ContainerConfiguration().WithParts(parts).CreateContainer();

        using AdhocWorkspace workspace = new(MefHostServices.Create(composition));

        ProjectId projectIdentifier = ProjectId.CreateNewId();

        (LanguageVersion languageVersion, bool substituted) = LanguageVersionResolver.Resolve(inputs.LanguageVersion, inputs.ProjectPath);

        if (substituted)
        {
            inputs.Log.LogMessage(
                MessageImportance.Normal,
                $"NetAgents formats {inputs.ProjectPath} with C# preview because this package cannot parse LangVersion {inputs.LanguageVersion}"
            );
        }

        OutputKind outputKind = inputs.OutputType switch
        {
            "Exe" => OutputKind.ConsoleApplication,
            "WinExe" => OutputKind.WindowsApplication,
            _ => OutputKind.DynamicallyLinkedLibrary,
        };

        CSharpParseOptions parseOptions = new(
            languageVersion,
            DocumentationMode.Diagnose,
            preprocessorSymbols: inputs.DefineConstants.Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
        );

        CSharpCompilationOptions compilationOptions = new(
            outputKind,
            allowUnsafe: inputs.AllowUnsafe,
            nullableContextOptions: NullableContextOptions.Enable,
            generalDiagnosticOption: ReportDiagnostic.Error
        );

        (AnalyzerReference[] references, string[] hosted, string[] unavailable) = AnalyzerCatalog.Load(analyzerPaths, loader);

        // Hosting them keeps the enforcement the consumer configured, so this stays out of a normal log.
        foreach (string announcement in hosted)
            inputs.Log.LogMessage(MessageImportance.Normal, announcement);

        // An analyzer the formatter cannot run is enforcement the consumer configured and does not get.
        foreach (string failure in unavailable)
            inputs.Log.LogWarning(failure);

        ProjectInfo information = ProjectInfo.Create(
            projectIdentifier,
            VersionStamp.Create(),
            inputs.AssemblyName,
            inputs.AssemblyName,
            LanguageNames.CSharp,
            filePath: inputs.ProjectPath,
            compilationOptions: compilationOptions,
            parseOptions: parseOptions,
            metadataReferences: inputs.References.Select(CreateReference),
            analyzerReferences: references
        );

        Project initial = workspace.CurrentSolution.AddProject(information).GetProject(projectIdentifier)
            ?? throw new InvalidOperationException(message: "The formatting project is missing.");

        Solution solution = initial.WithDefaultNamespace(inputs.RootNamespace).Solution;
        string projectDirectory = Path.GetDirectoryName(inputs.ProjectPath) ?? throw new InvalidOperationException(message: "The project directory is missing.");

        foreach (ITaskItem source in inputs.SourceFiles)
        {
            string path = GetPath(source);
            SourceText text = await ReadText(path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            string logicalPath = source.GetMetadata(metadataName: "Link");

            if (string.IsNullOrEmpty(logicalPath))
                logicalPath = Path.GetRelativePath(projectDirectory, path);

            string[] segments = logicalPath.Replace(oldChar: '\\', newChar: '/').Split(separator: '/');
            string[] folders = segments[0] == ".." ? [] : [.. segments.Take(segments.Length - 1)];
            solution = solution.AddDocument(DocumentId.CreateNewId(projectIdentifier), Path.GetFileName(path), text, folders, path);
        }

        foreach (ITaskItem configuration in inputs.ConfigurationFiles)
        {
            string path = GetPath(configuration);
            SourceText text = await ReadText(path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            solution = solution.AddAnalyzerConfigDocument(DocumentId.CreateNewId(projectIdentifier), Path.GetFileName(path), text, filePath: path);
        }

        foreach (ITaskItem additional in inputs.AdditionalFiles)
        {
            string path = GetPath(additional);
            SourceText text = await ReadText(path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            solution = solution.AddAdditionalDocument(DocumentId.CreateNewId(projectIdentifier), Path.GetFileName(path), text, filePath: path);
        }

        if (!workspace.TryApplyChanges(solution))
            throw new InvalidOperationException(message: "The formatting workspace could not be initialized.");

        Project original = workspace.CurrentSolution.GetProject(projectIdentifier)
            ?? throw new InvalidOperationException(message: "The formatting project is missing.");

        ImmutableArray<DiagnosticAnalyzer> analyzers = [.. original.AnalyzerReferences.SelectMany(static reference => reference.GetAnalyzers(LanguageNames.CSharp).ToArray())];
        CodeFixProvider[] providers = [.. composition.GetExports<CodeFixProvider>()];
        (Project changed, ImmutableArray<Diagnostic> blocking) = await CodeFixRunner
            .Fix(original, analyzers, providers, fault => inputs.Log.LogWarning(fault), cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        List<string> rewritten = [];

        foreach (Document document in changed.Documents)
        {
            Document previous = original.GetDocument(document.Id) ?? throw new InvalidOperationException(message: "A code fix unexpectedly added a document.");
            SourceText before = await previous.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            SourceText after = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (!before.ContentEquals(after) && document.FilePath is not null)
            {
                await File.WriteAllTextAsync(
                    document.FilePath,
                    after.ToString(),
                    after.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken
                ).ConfigureAwait(continueOnCapturedContext: false);
                rewritten.Add(GetDisplayPath(projectDirectory, document.FilePath));
            }
        }

        Report(inputs, rewritten, blocking);
    }

    private static string GetDisplayPath(string projectDirectory, string path)
    {
        string relative = Path.GetRelativePath(projectDirectory, path);

        // A linked file outside the project keeps its absolute path instead of a relative walk.
        return relative.StartsWith(value: "..", StringComparison.Ordinal) ? path : relative;
    }

    private static string GetPath(ITaskItem item)
    {
        return item.GetMetadata(metadataName: "FullPath");
    }

    private static async Task<SourceText> ReadText(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return SourceText.From(bytes, bytes.Length, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // Formatting runs before the compiler, so a build that fails afterwards still leaves these edits
    // behind. Naming every rewritten file keeps that visible to reviewers and dirty-tree checks.
    private static void Report(FormatProject inputs, List<string> rewritten, ImmutableArray<Diagnostic> blocking)
    {
        if (rewritten.Count == 0)
            return;

        foreach (string path in rewritten)
            inputs.Log.LogMessage(MessageImportance.High, "NetAgents rewrote " + path);

        if (blocking.IsEmpty)
            return;

        string[] identifiers = [.. blocking.Select(static diagnostic => diagnostic.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string files = rewritten.Count == 1 ? "file" : "files";
        string rewriteCount = rewritten.Count.ToString(CultureInfo.InvariantCulture);

        // The identifiers are what this formatter could not repair; the compiler reports the full set.
        inputs.Log.LogMessage(
            MessageImportance.High,
            $"NetAgents rewrote {rewriteCount} {files} before this build failed on diagnostics no fix repairs: {string.Join(separator: ", ", identifiers)}"
        );

        foreach (string path in rewritten)
            inputs.Log.LogMessage(MessageImportance.High, "  " + path);
    }
}
