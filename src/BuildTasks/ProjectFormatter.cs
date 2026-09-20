using System.Collections.Immutable;
using System.Composition.Hosting;
using System.Reflection;
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

        Assembly[] assemblies = [.. MefHostServices.DefaultAssemblies,
            typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions).Assembly,
            .. analyzerPaths.Concat(styleFixes).Concat(featurePaths).Select(loader.LoadFromPath)];

        using CompositionHost composition = new ContainerConfiguration().WithParts(assemblies.Distinct().SelectMany(GetLoadableTypes)).CreateContainer();

        using AdhocWorkspace workspace = new(MefHostServices.Create(composition));

        ProjectId projectIdentifier = ProjectId.CreateNewId();

        if (!LanguageVersionFacts.TryParse(inputs.LanguageVersion, out LanguageVersion languageVersion))
            throw new InvalidOperationException(message: "The configured C# language version is invalid.");

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
            analyzerReferences: analyzerPaths.Select(path => new AnalyzerFileReference(path, loader))
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
        Project changed = await CodeFixRunner.Fix(original, analyzers, providers, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

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
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // Analyzer assemblies can embed optional helpers for older Roslyn interfaces.
            return exception.Types.OfType<Type>();
        }
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
}
