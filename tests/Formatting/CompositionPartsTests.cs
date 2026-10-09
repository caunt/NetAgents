using System.Composition;
using System.Composition.Hosting;
using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>Checks the parts the formatter composes out of analyzer and code-fix assemblies.</summary>
public sealed class CompositionPartsTests
{
    private const string DependencySource = """
        public sealed class Absent
        {
        }
        """;

    private const string LanguagePartsSource = """
        using System;
        using System.Collections.Immutable;
        using System.Threading.Tasks;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CodeFixes;

        public abstract class BaseFix : CodeFixProvider
        {
            public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("TEST0001");
            public override Task RegisterCodeFixesAsync(CodeFixContext context) => Task.CompletedTask;
        }

        [ExportCodeFixProvider(LanguageNames.CSharp, Name = "CSharp")]
        public sealed class CSharpFix : BaseFix { }

        [ExportCodeFixProvider(LanguageNames.CSharp, LanguageNames.VisualBasic, Name = "Shared")]
        public sealed class SharedFix : BaseFix { }

        [ExportCodeFixProvider(LanguageNames.VisualBasic, Name = "VisualBasic")]
        public sealed class VisualBasicFix : BaseFix
        {
            public VisualBasicFix() => throw new InvalidOperationException("Wrong language provider was instantiated.");
        }
        """;

    private const string PartsSource = """
        using System;
        using System.Composition;

        [AttributeUsage(AttributeTargets.All)]
        public sealed class MarkerAttribute : Attribute
        {
            public MarkerAttribute(Type marked)
            {
                Marked = marked;
            }

            public Type Marked { get; }
        }

        [Export(typeof(ICloneable))]
        public sealed class BrokenPart : ICloneable
        {
            public object Clone()
            {
                return this;
            }

            [Marker(typeof(Absent))]
            public void Satisfy()
            {
            }
        }

        [Export(typeof(ICloneable))]
        public sealed class HealthyPart : ICloneable
        {
            public object Clone()
            {
                return this;
            }
        }
        """;

    /// <summary>Composes C# and shared fixes without instantiating foreign-language exports.</summary>
    [Fact]
    public void FiltersCodeFixesByExportedLanguage()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory(prefix: "netagents-language-parts-");

        try
        {
            string partsPath = Path.Combine(workspace.FullName, path2: "LanguageParts.dll");
            Compile(
                partsPath,
                LanguagePartsSource,
                [typeof(ExportAttribute).Assembly.Location, typeof(CodeFixProvider).Assembly.Location, typeof(Diagnostic).Assembly.Location]
            );

            using DependencyLoader loader = new(workspace.FullName);

            Assembly parts = loader.LoadFromAssemblyPath(partsPath);
            CompositionParts.CompositionSelection selection = CompositionParts.Select([parts]);
            Assert.Empty(selection.Skipped);
            Assert.DoesNotContain(selection.Types, static type => type.Name == "VisualBasicFix");

            using CompositionHost container = new ContainerConfiguration().WithParts(selection.Types).CreateContainer();

            string[] providers = [.. container.GetExports<CodeFixProvider>().Select(static provider => provider.GetType().Name).Order(StringComparer.Ordinal)];
            Assert.Equal(["CSharpFix", "SharedFix"], providers);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Skips the one export whose member attribute names a type this runtime cannot load, the way the SDK
    /// ships ASP.NET Core code fixes that only resolve inside an IDE.
    /// </summary>
    [Fact]
    public void SkipsExportNamingUnresolvedType()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory(prefix: "netagents-composition-");

        try
        {
            string dependencyPath = Path.Combine(workspace.FullName, path2: "Dependency.dll");
            string partsPath = Path.Combine(workspace.FullName, path2: "Parts.dll");

            Compile(dependencyPath, DependencySource, []);
            Compile(partsPath, PartsSource, [dependencyPath, typeof(ExportAttribute).Assembly.Location]);

            // The rebuilt dependency still loads, so the attribute argument fails to resolve exactly the way
            // Microsoft.AspNetCore.App.CodeFixes fails without Microsoft.CodeAnalysis.ExternalAccess.AspNetCore.
            Compile(dependencyPath, source: "public sealed class Remaining { }", []);

            using DependencyLoader loader = new(workspace.FullName);

            Assembly parts = loader.LoadFromAssemblyPath(partsPath);

            Exception failure = Assert.ThrowsAny<Exception>(() => Compose(parts.GetTypes()));
            Assert.Contains(expectedSubstring: "Absent", failure.ToString(), StringComparison.Ordinal);

            CompositionParts.CompositionSelection selection = CompositionParts.Select([parts]);

            Assert.Equal(expected: "BrokenPart", Assert.Single(selection.Skipped));
            Assert.Contains(selection.Types, static type => type.Name == "HealthyPart");
            Assert.DoesNotContain(selection.Types, static type => type.Name == "BrokenPart");
            ICloneable composed = Assert.Single(Compose(selection.Types));
            Assert.Equal(expected: "HealthyPart", composed.GetType().Name);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    private static void Compile(string assemblyPath, string source, string[] references)
    {
        MetadataReference[] metadata = [.. AnalyzerTestHarness.References, .. references.Select(static path => MetadataReference.CreateFromFile(path))];

        CSharpCompilation compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(assemblyPath),
            [CSharpSyntaxTree.ParseText(source)],
            metadata,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        EmitResult result = compilation.Emit(assemblyPath);
        Diagnostic[] diagnostics = [.. result.Diagnostics];
        Assert.True(result.Success, string.Join(separator: "\n", diagnostics));
    }

    private static ICloneable[] Compose(IEnumerable<Type> parts)
    {
        using CompositionHost container = new ContainerConfiguration().WithParts(parts).CreateContainer();

        return [.. container.GetExports<ICloneable>()];
    }

    // Only the rebuilt dependency resolves here; everything else falls back to the test's own context so the
    // composition attributes keep one identity.
    private sealed class DependencyLoader(string directory) : AssemblyLoadContext(isCollectible: true), IDisposable
    {
        public void Dispose()
        {
            Unload();
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string candidate = Path.Combine(directory, assemblyName.Name + ".dll");

            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}
