using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.CodeFixes.Formatting;
using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.Formatting;

/// <summary>Checks formatter progress with unresolved compiler diagnostics.</summary>
public sealed class CodeFixRunnerTests
{
    // The layout NETAGENTS0020 and NETAGENTS0016 both accept for a lambda holding statements.
    private const string ExpandedLambda = "System.Threading.Tasks.Task.Run(\n            () =>\n            {\n"
        + "                int length = System.Environment.ProcessorCount;\n\n                return length > 0;\n            }\n        );";

    /// <summary>Appends the final newline the document already uses instead of a bare line feed.</summary>
    [Fact]
    public async Task AppendsMatchingFinalNewline()
    {
        using AdhocWorkspace workspace = new();

        Project project = workspace.AddProject(name: "Endings", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Example.cs", SourceText.From(text: "class Example\r\n{\r\n}"), filePath: "/Example.cs").Project
            .AddAnalyzerConfigDocument(name: ".globalconfig", SourceText.From(text: "is_global = true\ninsert_final_newline = true\n"), filePath: "/.globalconfig").Project;

        Project result = (await CodeFixRunner.Fix(project, [], [], static fault => Assert.Fail(fault), CancellationToken.None)).Project;
        SourceText text = await result.Documents.Single().GetTextAsync();

        Assert.EndsWith(expectedEndString: "}\r\n", text.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Settles argument layout and statement spacing on a lambda holding statements (#38).</summary>
    /// <param name="statement">How the statement passing the lambda is written before the run.</param>
    [Theory]
    [InlineData(ExpandedLambda)]
    [InlineData("System.Threading.Tasks.Task.Run(() => { int length = System.Environment.ProcessorCount; return length > 0; });")]
    public async Task ConvergesWithStatementSpacing(string statement)
    {
        const string method = "class Example\n{\n    System.Threading.Tasks.Task<bool> Execute()\n    {\n        return STATEMENT\n    }\n}\n";

        using AdhocWorkspace workspace = new();

        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(
            CreateFieldProject(workspace, method.Replace(oldValue: "STATEMENT", statement, StringComparison.Ordinal)),
            [new ArgumentLayoutAnalyzer(), new StatementSpacingAnalyzer()],
            [new ArgumentLayoutCodeFixProvider(), new StatementSpacingCodeFixProvider()],
            static fault => Assert.Fail(fault),
            CancellationToken.None
        );

        string formatted = (await result.Project.Documents.Single().GetTextAsync()).ToString();

        Assert.Equal(method.Replace(oldValue: "STATEMENT", ExpandedLambda, StringComparison.Ordinal), formatted);
        Assert.True(result.Blocking.IsEmpty);
        Assert.Empty(await AnalyzerTestHarness.Analyze(new ArgumentLayoutAnalyzer(), formatted));
        Assert.Empty(await AnalyzerTestHarness.Analyze(new StatementSpacingAnalyzer(), formatted));
    }

    /// <summary>Keeps a rewrite whose intermediate error a later pass repairs.</summary>
    [Fact]
    public async Task KeepsCascadingFixesThatSettle()
    {
        using AdhocWorkspace workspace = new();

        const string source = "class Example { private static int Value; }";

        CodeFixProvider[] providers = [
            new ModifierFix(CascadeAnalyzer.PrivateIdentifier, replaced: "private", replacement: "public"),
            new ModifierFix(CascadeAnalyzer.PublicIdentifier, replaced: "public", replacement: "internal")];

        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(
            CreateFieldProject(workspace, source),
            [new CascadeAnalyzer()],
            providers,
            static fault => Assert.Fail(fault),
            CancellationToken.None
        );

        // The first pass introduces the second rule and the second pass settles it, so the run keeps its work.
        Assert.True(result.Introduced.IsEmpty);
        Assert.True(result.Blocking.IsEmpty);
        Assert.Contains(
            expectedSubstring: "internal static int Value",
            (await result.Project.Documents.Single().GetTextAsync()).ToString(),
            StringComparison.Ordinal
        );
    }

    /// <summary>Moves an internal type when nothing outside the assembly can name it.</summary>
    [Fact]
    public async Task MovesInternalTypesWhenTheAssemblyKeepsThemToItself()
    {
        using AdhocWorkspace workspace = new();

        Project project = CreateNamespacedProject(workspace, accessibility: "internal", internalsShared: false);
        MarkerAnalyzer analyzer = new();

        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(project, [analyzer], [new NamespaceFix()], static fault => Assert.Fail(fault), CancellationToken.None);

        string formatted = (await result.Project.Documents.First().GetTextAsync()).ToString();
        Assert.Contains(expectedSubstring: "namespace Consumer.Nested;", formatted, StringComparison.Ordinal);
    }

    /// <summary>Never offers a build-failing compiler error to a code-fix provider, leaving the compiler to report it.</summary>
    /// <param name="body">The member and statement text the compiler rejects.</param>
    /// <param name="survivor">The rejected text the formatter must leave in place.</param>
    /// <param name="diagnosticIdentifier">The compiler error the text produces.</param>
    [Theory]
    [InlineData("void Run() { Missing(); }", "Missing();", "CS0103")]
    [InlineData("void Run() { Sample sample = new(); }", "Sample sample = new();", "CS0246")]
    [InlineData(
        "static bool IsValid(System.Memory<byte>? data) => data.HasValue; void Run() { System.ReadOnlyMemory<byte>? document = null; IsValid(document); }",
        "IsValid(document);",
        "CS1503"
    )]
    [InlineData("Example() { } void Run() { Example example = new(0); }", "new(0);", "CS1729")]
    public async Task PreservesCompilerErrors(string body, string survivor, string diagnosticIdentifier)
    {
        using AdhocWorkspace workspace = new();

        Project project = CreateProject(workspace);
        string source = $"class Example {{ {body} }}";
        project = project.Documents.Single().WithText(SourceText.From(source)).Project;
        CompilerErrorFix provider = new(diagnosticIdentifier);

        Project result = (await CodeFixRunner.Fix(project, [], [provider], static fault => Assert.Fail(fault), CancellationToken.None)).Project;
        Compilation? compilation = await result.GetCompilationAsync();

        Assert.NotNull(compilation);
        Diagnostic error = Assert.Single(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal(diagnosticIdentifier, error.Id);
        Assert.Equal(expected: 0, provider.Attempts);
        Assert.Contains(survivor, (await result.Documents.Single().GetTextAsync()).ToString(), StringComparison.Ordinal);
    }

    /// <summary>Leaves the compiler error in place when its only provider cannot run.</summary>
    /// <param name="stage">The provider stage that fails.</param>
    [Theory]
    [InlineData("identifiers")]
    [InlineData("registration")]
    [InlineData("operations")]
    public async Task PreservesErrorWhenEveryProviderFails(string stage)
    {
        using AdhocWorkspace workspace = new();

        ThrowingFix provider = new(stage);

        Project result = (await CodeFixRunner.Fix(CreateProject(workspace), [new MarkerAnalyzer()], [provider], static fault => Assert.Fail(fault), CancellationToken.None))
            .Project;

        Compilation? compilation = await result.GetCompilationAsync();

        Assert.NotNull(compilation);
        Diagnostic error = Assert.Single(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal(expected: "CS0122", error.Id);

        // The skipped accessibility repair would have replaced this modifier.
        Assert.Contains(
            expectedSubstring: "private static int Value",
            (await result.Documents.Single().GetTextAsync()).ToString(),
            StringComparison.Ordinal
        );
    }

    /// <summary>Never offers a compiler warning the build escalates to an error, keeping the cast the fix would add out.</summary>
    [Fact]
    public async Task PreservesEscalatedCompilerWarning()
    {
        using AdhocWorkspace workspace = new();

        // Consumer builds escalate every warning, so a nullable remark fails the build exactly like an error.
        const string source = "class Example { static int Read(int? value) { return (int)value; } }\n";

        Project project = workspace.AddProject(name: "Escalated", LanguageNames.CSharp)
            .WithCompilationOptions(
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable,
                    generalDiagnosticOption: ReportDiagnostic.Error
                )
            )
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Example.cs", SourceText.From(source), filePath: "Example.cs").Project;

        CompilerErrorFix provider = new(identifier: "CS8629");
        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(project, [], [provider], static fault => Assert.Fail(fault), CancellationToken.None);

        Assert.Equal(expected: "CS8629", result.Blocking.Single().Id);
        Assert.Equal(expected: 0, provider.Attempts);
        Assert.Contains(expectedSubstring: "(int)value", (await result.Project.Documents.Single().GetTextAsync()).ToString(), StringComparison.Ordinal);
    }

    /// <summary>Applies the single-document action when project-wide fixing cannot run.</summary>
    /// <param name="stage">The project-wide stage that fails.</param>
    [Theory]
    [InlineData("scopes")]
    [InlineData("fixAll")]
    public async Task PreservesFixWhenProjectWideFixingFails(string stage)
    {
        using AdhocWorkspace workspace = new();

        ThrowingFix provider = new(stage);

        Project result = (await CodeFixRunner.Fix(
            CreateProject(workspace),
            [new MethodSpacingAnalyzer(), new MarkerAnalyzer()],
            [provider],
            static fault => Assert.Fail(fault),
            CancellationToken.None
        )).Project;

        Compilation? compilation = await result.GetCompilationAsync();

        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation.GetDiagnostics().ToArray(), static diagnostic => diagnostic.Id == "CS0122");
        Assert.InRange(provider.Attempts, low: 1, high: 3);
    }

    /// <summary>Leaves generated documents untouched, including the unhyphenated header the SDK emits.</summary>
    /// <param name="header">The generated-code marker introducing the document.</param>
    [Theory]
    [InlineData("// <autogenerated />")]
    [InlineData("// <auto-generated />")]
    public async Task PreservesGeneratedDocuments(string header)
    {
        using AdhocWorkspace workspace = new();

        string generated = header + "\nclass Generated{   void Run( ) { } }\n";

        Project project = CreateProject(workspace)
            .AddDocument(name: "Generated.cs", SourceText.From(generated), filePath: "Generated.cs").Project;

        Project result = (await CodeFixRunner.Fix(project, [], [], static fault => Assert.Fail(fault), CancellationToken.None)).Project;
        Document document = result.Documents.Single(static candidate => candidate.Name == "Generated.cs");

        Assert.Equal(generated, (await document.GetTextAsync()).ToString());
    }

    /// <summary>Still applies a hidden compiler suggestion's fix, which no build fails for.</summary>
    [Fact]
    public async Task PreservesHiddenSuggestionFix()
    {
        using AdhocWorkspace workspace = new();

        // CS8019 is a hidden suggestion rather than a build failure, so removing the import stays formatting.
        Project project = CreateProject(workspace);
        const string source = "using System;\nclass Example { int Read() => 0; }\n";
        project = project.Documents.Single().WithText(SourceText.From(source)).Project;
        SuggestionFix provider = new(source: "class Example { int Read() => 0; }\n");

        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(project, [], [provider], static fault => Assert.Fail(fault), CancellationToken.None);

        Assert.True(result.Blocking.IsEmpty);
        Assert.DoesNotContain(
            expectedSubstring: "using System;",
            (await result.Project.Documents.Single().GetTextAsync()).ToString(),
            StringComparison.Ordinal
        );
    }

    /// <summary>Allows existing compiler errors to move when a fix changes preceding text.</summary>
    [Fact]
    public async Task PreservesMovedCompilerError()
    {
        using AdhocWorkspace workspace = new();

        Project project = CreateProject(workspace);
        project = project.Documents.Single().WithText(SourceText.From(text: "class Example { void Run() { int marker = 0; Missing(); } }")).Project;
        ReplacementFix provider = new(source: "class Example { void Run() { Missing(); } }");

        Project result = (await CodeFixRunner.Fix(project, [], [provider], static fault => Assert.Fail(fault), CancellationToken.None)).Project;
        Compilation? compilation = await result.GetCompilationAsync();

        Assert.NotNull(compilation);
        Diagnostic error = Assert.Single(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal(expected: "CS0103", error.Id);
        Assert.Equal(expected: 1, provider.Attempts);
    }

    /// <summary>Hands back the original source when the run settles on errors it introduced.</summary>
    [Fact]
    public async Task PreservesSourceWhenFixesIntroduceErrors()
    {
        using AdhocWorkspace workspace = new();

        const string source = "class Example { private static int Value; }";
        ModifierFix provider = new(CascadeAnalyzer.PrivateIdentifier, replaced: "private", replacement: "public");

        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(
            CreateFieldProject(workspace, source),
            [new CascadeAnalyzer()],
            [provider],
            static fault => Assert.Fail(fault),
            CancellationToken.None
        );

        // Nothing repairs the rule the fix triggered, so the rewrite is withheld and named instead.
        Assert.Equal(source, (await result.Project.Documents.Single().GetTextAsync()).ToString());
        Assert.Equal(CascadeAnalyzer.PublicIdentifier, result.Introduced.Single().Id);
        Assert.Equal(CascadeAnalyzer.PrivateIdentifier, result.Blocking.Single().Id);
        Assert.NotNull(result.IntroducedBy);
        Assert.Contains(expectedSubstring: "Replace private with public", result.IntroducedBy, StringComparison.Ordinal);
    }

    /// <summary>Still applies valid automatic fixes to analyzer errors.</summary>
    [Theory]
    [InlineData("valid")]
    [InlineData("noop")]
    public async Task PreservesValidAnalyzerFix(string firstAction)
    {
        using AdhocWorkspace workspace = new();

        AccessibilityFix provider = new(mode: "valid");
        CodeFixProvider[] providers = firstAction == "noop" ? [new AccessibilityFix(mode: "noop"), provider] : [provider];

        Project result = (await CodeFixRunner.Fix(
            CreateProject(workspace),
            [new MethodSpacingAnalyzer(), new MarkerAnalyzer()],
            providers,
            static fault => Assert.Fail(fault),
            CancellationToken.None
        )).Project;

        Compilation? compilation = await result.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation.GetDiagnostics().ToArray(), static diagnostic => diagnostic.Id == "CS0122");
        Assert.Equal(expected: 1, provider.Attempts);
    }

    /// <summary>Refuses to move a public type to another namespace, which no later project can see.</summary>
    /// <param name="accessibility">The accessibility of the type the fix would move.</param>
    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    public async Task RefusesToMoveTypesOtherProjectsCanName(string accessibility)
    {
        using AdhocWorkspace workspace = new();

        Project project = CreateNamespacedProject(workspace, accessibility, internalsShared: true);
        MarkerAnalyzer analyzer = new();
        List<string> messages = [];

        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(
            project,
            [analyzer],
            [new NamespaceFix()],
            static fault => Assert.Fail(fault),
            CancellationToken.None,
            new(project.Name, messages.Add)
        );

        string formatted = (await result.Project.Documents.First().GetTextAsync()).ToString();
        Assert.Contains(expectedSubstring: "namespace Consumer;", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "namespace Consumer.Nested;", formatted, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "private static int Value;", formatted, StringComparison.Ordinal);
        Assert.Contains(messages, static message => message.Contains(value: "other projects can name", StringComparison.Ordinal));
    }

    /// <summary>Rejects new compiler errors before accepting an automatic fix.</summary>
    [Theory]
    [InlineData("", "0 = 1;", "CS0131")]
    [InlineData("Missing();", "0 = 1;", "CS0131")]
    [InlineData("Missing();", "Missing(); Missing();", "CS0103")]
    public async Task RejectsCompilerBreakingFix(string before, string after, string diagnosticIdentifier)
    {
        using AdhocWorkspace workspace = new();

        Project project = CreateProject(workspace);
        Document document = project.Documents.Single();
        string source = $"class Example {{ void Run() {{ int marker = 0; {before} }} }}";
        project = document.WithText(SourceText.From(source)).Project;
        ReplacementFix provider = new($"class Example {{ void Run() {{ {after} }} }}");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CodeFixRunner.Fix(project, [], [provider], static fault => Assert.Fail(fault), CancellationToken.None));

        Assert.Contains(expectedSubstring: "introduced compiler errors", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "Replace method body", exception.Message, StringComparison.Ordinal);
        Assert.Contains(diagnosticIdentifier, exception.Message, StringComparison.Ordinal);
        Assert.Equal(expected: 1, provider.Attempts);
        Assert.Equal(source, (await project.Documents.Single().GetTextAsync()).ToString());
    }

    /// <summary>Reports the errors that remain so the caller can name the files it already rewrote.</summary>
    [Fact]
    public async Task ReportsBlockedDiagnostics()
    {
        using AdhocWorkspace workspace = new();

        CodeFixRunner.CodeFixResult blockedResult = await CodeFixRunner.Fix(CreateProject(workspace), [], [], static fault => Assert.Fail(fault), CancellationToken.None);

        Assert.Equal(expected: "CS0122", blockedResult.Blocking.Single().Id);

        // The unrelated whitespace repair still lands, which is what a failing build writes to disk.
        Assert.Contains(
            expectedSubstring: "private static int Value",
            (await blockedResult.Project.Documents.Single().GetTextAsync()).ToString(),
            StringComparison.Ordinal
        );

        Project clean = workspace.AddProject(name: "Clean", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Clean.cs", SourceText.From(text: "class Clean\n{\n}\n"), filePath: "Clean.cs").Project;

        CodeFixRunner.CodeFixResult cleanResult = await CodeFixRunner.Fix(clean, [], [], static fault => Assert.Fail(fault), CancellationToken.None);

        Assert.True(cleanResult.Blocking.IsEmpty);
        Assert.Equal(expected: "class Clean\n{\n}\n", (await cleanResult.Project.Documents.Single().GetTextAsync()).ToString());
    }

    /// <summary>Never blames a dropped suggestion for a run the blocking errors ended.</summary>
    [Fact]
    public async Task ReportsBlockedDiagnosticsWhenSuggestionStalls()
    {
        using AdhocWorkspace workspace = new();

        // CS8019 is a hidden suggestion, so the action offered for it never explains a stopped run.
        Project project = CreateProject(workspace);
        const string source = "using System;\nclass Hidden { private static int Value; }\nclass Consumer { int Read() => Hidden.Value; }\n";
        project = project.Documents.Single().WithText(SourceText.From(source)).Project;
        StalledSuggestionFix provider = new();

        CodeFixRunner.CodeFixResult result = await CodeFixRunner.Fix(project, [], [provider], static fault => Assert.Fail(fault), CancellationToken.None);

        Assert.Equal(expected: "CS0122", result.Blocking.Single().Id);
        Assert.InRange(provider.Attempts, low: 1, high: 3);
        Assert.Contains(
            expectedSubstring: "using System;",
            (await result.Project.Documents.Single().GetTextAsync()).ToString(),
            StringComparison.Ordinal
        );
    }

    /// <summary>Reports stalled actions without exhausting the pass limit.</summary>
    [Theory]
    [InlineData("noop")]
    [InlineData("whitespace")]
    [InlineData("cycle")]
    public async Task ReportsStalledCompilerFix(string mode)
    {
        using AdhocWorkspace workspace = new();

        Project project = CreateProject(workspace);
        AccessibilityFix provider = new(mode);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CodeFixRunner.Fix(
                project,
                [new MethodSpacingAnalyzer(), new MarkerAnalyzer()],
                [provider],
                static fault => Assert.Fail(fault),
                CancellationToken.None
            )
        );

        Assert.Contains(expectedSubstring: "CS0122", exception.Message, StringComparison.Ordinal);
        Assert.Matches(expectedRegexPattern: @"Example\.cs\([1-9][0-9]*,[1-9][0-9]*\): error CS0122", exception.Message);
        Assert.Contains(expectedSubstring: "Attempt accessibility repair", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "fix passes", exception.Message, StringComparison.Ordinal);

        // A blank segment reaches MSBuild as a second build error carrying no message at all.
        Assert.DoesNotContain(Environment.NewLine + Environment.NewLine, exception.Message, StringComparison.Ordinal);
        Assert.False(exception.Message.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        Assert.InRange(provider.Attempts, low: 1, high: 3);
    }

    /// <summary>Drops a suggestion's compiler-breaking action instead of failing an otherwise clean build.</summary>
    [Fact]
    public async Task SkipsCompilerBreakingSuggestionFix()
    {
        using AdhocWorkspace workspace = new();

        // CS8019 is a hidden compiler suggestion, so nothing about this document has to be repaired.
        Project project = CreateProject(workspace);
        const string source = "using System;\nclass Example { int Read() => 0; }";
        project = project.Documents.Single().WithText(SourceText.From(source)).Project;
        SuggestionFix provider = new(source: "using System;\nclass Example { int Read() => \"text\"; }");

        Project result = (await CodeFixRunner.Fix(project, [], [provider], static fault => Assert.Fail(fault), CancellationToken.None)).Project;

        Assert.Equal(source, (await result.Documents.Single().GetTextAsync()).ToString());
        Assert.InRange(provider.Attempts, low: 1, high: 2);
    }

    /// <summary>Skips a failing provider while another provider still repairs the compiler error.</summary>
    /// <param name="stage">The provider stage that fails.</param>
    [Theory]
    [InlineData("identifiers")]
    [InlineData("registration")]
    [InlineData("operations")]
    public async Task SkipsFailingCodeFixProvider(string stage)
    {
        using AdhocWorkspace workspace = new();

        ThrowingFix throwing = new(stage);
        AccessibilityFix working = new(mode: "valid");
        int expectedAttempts = stage == "identifiers" ? 0 : 1;

        Project result = (await CodeFixRunner.Fix(
            CreateProject(workspace),
            [new MethodSpacingAnalyzer(), new MarkerAnalyzer()],
            [throwing, working],
            static fault => Assert.Fail(fault),
            CancellationToken.None
        )).Project;

        Compilation? compilation = await result.GetCompilationAsync();

        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation.GetDiagnostics().ToArray(), static diagnostic => diagnostic.Id == "CS0122");
        Assert.Equal(expected: 1, working.Attempts);
        Assert.Equal(expectedAttempts, throwing.Attempts);
    }

    private static Project CreateFieldProject(AdhocWorkspace workspace, string source)
    {
        return workspace.AddProject(name: "Cascade", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Example.cs", SourceText.From(source), filePath: "Example.cs").Project;
    }

    private static Project CreateNamespacedProject(AdhocWorkspace workspace, string accessibility, bool internalsShared)
    {
        string source = "namespace Consumer;\n\n" + accessibility + " sealed class Moved\n{\n    private static int Value;\n}\n";

        Project project = workspace.AddProject(name: "Namespaces", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Moved.cs", SourceText.From(source), filePath: "Moved.cs").Project;

        return !internalsShared
            ? project
            : project.AddDocument(
                name: "Visibility.cs",
                SourceText.From(text: "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Friend\")]\n"),
                filePath: "Visibility.cs"
            ).Project;
    }

    private static Project CreateProject(AdhocWorkspace workspace)
    {
        return workspace.AddProject(name: "Progress", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(
                name: "Example.cs",
                SourceText.From(text: "class Hidden { private static int Value; } class Consumer { int Read() => Hidden.Value; }"),
                filePath: "Example.cs"
            ).Project;
    }

    private sealed class AccessibilityFix(string mode) : CodeFixProvider
    {
        public int Attempts { get; private set; }

        public override ImmutableArray<string> FixableDiagnosticIds => [MarkerAnalyzer.Identifier];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            Attempts++;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Attempt accessibility repair",
                    async cancellationToken =>
                    {
                        SourceText text = await context.Document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                        string source = text.ToString();

                        string changed = mode switch
                        {
                            "valid" => source.Replace(oldValue: "private", newValue: "public", StringComparison.Ordinal),
                            "whitespace" => source.Replace(oldValue: "class Hidden", newValue: "class  Hidden", StringComparison.Ordinal),
                            "cycle" => source.Contains(value: "class Consumer", StringComparison.Ordinal)
                                ? source.Replace(oldValue: "class Consumer", newValue: "class Alternate", StringComparison.Ordinal)
                                : source.Replace(oldValue: "class Alternate", newValue: "class Consumer", StringComparison.Ordinal),
                            _ => source,
                        };

                        return context.Document.WithText(SourceText.From(changed));
                    }
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }

    /// <summary>Reports one rule for a private field and another for a public one.</summary>
    [SuppressMessage(
        "MicrosoftCodeAnalysisCorrectness",
        "RS1001",
        Justification = "This private test analyzer is constructed explicitly and must not be discovered by compiler hosts."
    )]
    [SuppressMessage(
        "MicrosoftCodeAnalysisReleaseTracking",
        "RS2008",
        Justification = "These private instrumentation rules are only used in tests and are not shipped."
    )]
    private sealed class CascadeAnalyzer : DiagnosticAnalyzer
    {
        public const string PrivateIdentifier = "TEST0002";

        public const string PublicIdentifier = "TEST0003";

        private static readonly DiagnosticDescriptor PrivateRule = new(
            PrivateIdentifier,
            title: "Private field",
            messageFormat: "Private field",
            category: "Testing",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor PublicRule = new(
            PublicIdentifier,
            title: "Public field",
            messageFormat: "Public field",
            category: "Testing",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [PrivateRule, PublicRule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                static node =>
                {
                    FieldDeclarationSyntax field = (FieldDeclarationSyntax)node.Node;

                    if (field.Modifiers.Any(SyntaxKind.PrivateKeyword))
                        node.ReportDiagnostic(Diagnostic.Create(PrivateRule, field.GetLocation()));

                    if (field.Modifiers.Any(SyntaxKind.PublicKeyword))
                        node.ReportDiagnostic(Diagnostic.Create(PublicRule, field.GetLocation()));
                },
                SyntaxKind.FieldDeclaration
            );
        }
    }

    /// <summary>Stands in for the Roslyn fixes that answer a compiler error by generating code.</summary>
    /// <param name="identifier">The compiler diagnostic this provider claims.</param>
    private sealed class CompilerErrorFix(string identifier) : CodeFixProvider
    {
        public int Attempts { get; private set; }

        public override ImmutableArray<string> FixableDiagnosticIds => [identifier];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            Attempts++;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Declare the missing code",
                    cancellationToken => Task.FromResult(context.Document.WithText(SourceText.From(text: "class Example { void Run() { } }")))
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }

    /// <summary>Reports an analyzer error on a private field, which the accessibility repair removes.</summary>
    [SuppressMessage(
        "MicrosoftCodeAnalysisCorrectness",
        "RS1001",
        Justification = "This private test analyzer is constructed explicitly and must not be discovered by compiler hosts."
    )]
    [SuppressMessage(
        "MicrosoftCodeAnalysisReleaseTracking",
        "RS2008",
        Justification = "This private instrumentation rule is only used in tests and is not shipped."
    )]
    private sealed class MarkerAnalyzer : DiagnosticAnalyzer
    {
        public const string Identifier = "TEST0001";

        private static readonly DiagnosticDescriptor Rule = new(
            Identifier,
            title: "Private field",
            messageFormat: "Private field",
            category: "Testing",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                static node =>
                {
                    FieldDeclarationSyntax field = (FieldDeclarationSyntax)node.Node;

                    if (field.Modifiers.Any(SyntaxKind.PrivateKeyword))
                        node.ReportDiagnostic(Diagnostic.Create(Rule, field.GetLocation()));
                },
                SyntaxKind.FieldDeclaration
            );
        }
    }

    /// <summary>Replaces one modifier with another, which can satisfy one rule and trip the next.</summary>
    /// <param name="identifier">The diagnostic this provider claims.</param>
    /// <param name="replaced">The modifier to replace.</param>
    /// <param name="replacement">The modifier to write instead.</param>
    private sealed class ModifierFix(string identifier, string replaced, string replacement) : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => [identifier];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Replace {replaced} with {replacement}",
                    async cancellationToken =>
                    {
                        SourceText text = await context.Document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                        return context.Document.WithText(SourceText.From(text.ToString().Replace(replaced, replacement, StringComparison.Ordinal)));
                    }
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }

    private sealed class NamespaceFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => [MarkerAnalyzer.Identifier];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Move the type to a nested namespace",
                    async cancellationToken =>
                    {
                        SourceText text = await context.Document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                        // Repairing the marker too lets an accepted move settle, so the run ends on
                        // the moved namespace rather than on a fix that keeps being offered.
                        string moved = text.ToString()
                            .Replace(oldValue: "namespace Consumer;", newValue: "namespace Consumer.Nested;", StringComparison.Ordinal)
                            .Replace(oldValue: "private static int Value;", newValue: "public static int Value;", StringComparison.Ordinal);

                        return context.Document.WithText(SourceText.From(moved));
                    }
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }

    private sealed class ReplacementFix(string source) : CodeFixProvider
    {
        public int Attempts { get; private set; }

        public override ImmutableArray<string> FixableDiagnosticIds => ["CS0219"];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            Attempts++;
            context.RegisterCodeFix(
                CodeAction.Create(title: "Replace method body", cancellationToken => Task.FromResult(context.Document.WithText(SourceText.From(source)))),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }

    /// <summary>Offers a suggestion fix that rewrites nothing, leaving the run without progress.</summary>
    private sealed class StalledSuggestionFix : CodeFixProvider
    {
        public int Attempts { get; private set; }

        public override ImmutableArray<string> FixableDiagnosticIds => ["CS8019"];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            Attempts++;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Keep the unnecessary using",
                    async cancellationToken =>
                    {
                        SourceText text = await context.Document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                        return context.Document.WithText(SourceText.From(text.ToString()));
                    }
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }

    /// <summary>Offers a suggestion fix that replaces the document with uncompilable source.</summary>
    private sealed class SuggestionFix(string source) : CodeFixProvider
    {
        public int Attempts { get; private set; }

        public override ImmutableArray<string> FixableDiagnosticIds => ["CS8019"];

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            Attempts++;
            context.RegisterCodeFix(
                CodeAction.Create(title: "Replace suggestion body", cancellationToken => Task.FromResult(context.Document.WithText(SourceText.From(source)))),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }
    }

    /// <summary>Reproduces a provider built against another Roslyn version, failing at one chosen stage.</summary>
    private sealed class ThrowingFix(string stage) : CodeFixProvider
    {
        public int Attempts { get; private set; }

        [SuppressMessage("Design", "CA1065", Justification = "A skewed provider fails exactly here, which is the behavior under test.")]
        public override ImmutableArray<string> FixableDiagnosticIds => stage == "identifiers" ? throw Skew() : [MarkerAnalyzer.Identifier];

        public override FixAllProvider? GetFixAllProvider()
        {
            return stage switch
            {
                "scopes" => new ThrowingFixAll(failOnScopes: true),
                "fixAll" => new ThrowingFixAll(failOnScopes: false),
                _ => null,
            };
        }

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            Attempts++;

            if (stage == "registration")
                throw Skew();

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Repair accessibility",
                    async cancellationToken =>
                    {
                        if (stage == "operations")
                            throw Skew();

                        SourceText text = await context.Document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                        string changed = text.ToString().Replace(oldValue: "private", newValue: "public", StringComparison.Ordinal);

                        return context.Document.WithText(SourceText.From(changed));
                    },
                    equivalenceKey: "NetAgentsThrowingFix"
                ),
                context.Diagnostics
            );

            return Task.CompletedTask;
        }

        private static MissingMethodException Skew()
        {
            return new MissingMethodException(message: "Method not found: 'Void Microsoft.CodeAnalysis.CodeGeneration.CodeGenerationContext..ctor()'.");
        }
    }

    private sealed class ThrowingFixAll(bool failOnScopes) : FixAllProvider
    {
        public override Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            throw new MissingMethodException(message: "Method not found: 'Void Microsoft.CodeAnalysis.CodeGeneration.CodeGenerationContext..ctor()'.");
        }

        public override IEnumerable<FixAllScope> GetSupportedFixAllScopes()
        {
            return failOnScopes
                ? throw new MissingMethodException(message: "Method not found: 'Void Microsoft.CodeAnalysis.CodeGeneration.CodeGenerationContext..ctor()'.")
                : base.GetSupportedFixAllScopes();
        }
    }
}
