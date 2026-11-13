using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.CodeFixes.Formatting;
using NetAgents.Analyzers.CodeFixes.StatementGroups;
using NetAgents.Analyzers.Formatting;
using NetAgents.Analyzers.StatementGroups;
using NetAgents.Analyzers.Tests.Infrastructure;
using NetAgents.BuildTasks;

namespace NetAgents.Analyzers.Tests.StatementGroups;

/// <summary>
/// Runs the formatter with statement grouping beside the other layout rules that place blank lines and line
/// breaks between statements, and checks they settle on one layout.
/// </summary>
public sealed class StatementGroupingConvergenceTests
{
    private const string Method = "using System;\nusing System.Threading.Tasks;\n\nclass Example\n{\n    private int _placed;\n\n"
        + "    async Task<int> Execute(int[] prices, Task save)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies a suggestion that turns statements NETAGENTS0016 keeps apart into one group is still applied, as
    /// IDE0031 does when two null-checked assignments become <c>builder?.Length = length;</c>: the blank line left
    /// between them is settled by the grouping fix instead of getting the suggestion rejected.
    /// </summary>
    [Fact]
    public async Task SettlesTheLayoutASuggestionLeaves()
    {
        const string source = "class Example\n{\n    private int _first;\n    private int _second;\n\n    void Execute(bool flag)\n    {\n"
            + "        if (flag)\n            _first = 1;\n\n        if (flag)\n            _second = 2;\n    }\n}\n";

        const string expected = "class Example\n{\n    private int _first;\n    private int _second;\n\n    void Execute(bool flag)\n    {\n"
            + "        _first = 1;\n        _second = 2;\n    }\n}\n";

        using AdhocWorkspace workspace = new();

        Project project = workspace.AddProject(name: "Suggestions", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Example.cs", SourceText.From(source), filePath: "Example.cs")
            .Project;

        CodeFixRunner.CodeFixResult run = await CodeFixRunner.Fix(project,
            analyzers: [new StatementGroupingAnalyzer(), new StatementSpacingAnalyzer(), new UnwrapAnalyzer()],
            providers: [new StatementGroupingCodeFixProvider(), new StatementSpacingCodeFixProvider(), new UnwrapFix()],
            static fault => Assert.Fail(fault),
            CancellationToken.None);

        Assert.Equal(expected, await DocumentText.Read(run.Project.Documents.Single()));
        Assert.True(run.Blocking.IsEmpty);
    }

    /// <summary>
    /// Verifies a method written with blank lines in arbitrary places settles into groups, control flow keeps the
    /// blank lines NETAGENTS0016 gives it, a comment above control flow moves its blank line above the comment,
    /// and a lambda spread by NETAGENTS0020 gets its groups inside the spread body.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the run.</param>
    [Theory]
    [InlineData("ArgumentNullException.ThrowIfNull(prices);\nint total = 0;\n\n_placed++;\nforeach (int price in prices)\n{\n    Console.WriteLine(price);\n"
        + "    total += price;\n}\n_placed = total;\n\n\nawait save;\n\nConsole.WriteLine(total);\nConsole.WriteLine(_placed);\nreturn total;",
        "ArgumentNullException.ThrowIfNull(prices);\n\nint total = 0;\n\n_placed++;\n\nforeach (int price in prices)\n{\n    Console.WriteLine(price);\n\n"
        + "    total += price;\n}\n\n_placed = total;\n\nawait save;\n\nConsole.WriteLine(total);\nConsole.WriteLine(_placed);\n\nreturn total;")]
    [InlineData("int total = prices.Length;\n// Nothing to save without prices.\n\nif (total == 0)\n    return 0;\n\nawait save;\n\nreturn total;",
        "int total = prices.Length;\n\n// Nothing to save without prices.\nif (total == 0)\n    return 0;\n\nawait save;\n\nreturn total;")]
    [InlineData("await Task.Run(() => { int length = prices.Length; Console.WriteLine(length); });\nreturn 0;",
        "await Task.Run(() =>\n{\n    int length = prices.Length;\n\n    Console.WriteLine(length);\n});\n\nreturn 0;")]
    public async Task SettlesWithTheOtherLayoutRules(string written, string expected)
    {
        DiagnosticAnalyzer[] analyzers = [new StatementGroupingAnalyzer(), new StatementSpacingAnalyzer(), new BlockPaddingAnalyzer(),
            new ControlFlowBracesAnalyzer(), new ArgumentLayoutAnalyzer()];

        CodeFixProvider[] providers = [new StatementGroupingCodeFixProvider(), new StatementSpacingCodeFixProvider(), new BlockPaddingCodeFixProvider(),
            new ControlFlowBracesCodeFixProvider(), new ArgumentLayoutCodeFixProvider()];

        using AdhocWorkspace workspace = new();

        Project project = workspace.AddProject(name: "Groups", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Example.cs", SourceText.From(Wrap(written)), filePath: "Example.cs")
            .Project;

        CodeFixRunner.CodeFixResult run = await CodeFixRunner.Fix(project, analyzers: [.. analyzers], providers: [.. providers], static fault => Assert.Fail(fault), CancellationToken.None);
        string formatted = await DocumentText.Read(run.Project.Documents.Single());

        Assert.Equal(Wrap(expected), formatted);
        Assert.True(run.Blocking.IsEmpty);

        foreach (DiagnosticAnalyzer analyzer in analyzers)
            Assert.Empty(await AnalyzerTestHarness.Analyze(analyzer, formatted));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }

    /// <summary>Suggests dropping the condition of an <c>if</c> without an <c>else</c>, keeping its statement.</summary>
    [SuppressMessage("MicrosoftCodeAnalysisCorrectness",
        "RS1001",
        Justification = "This private test analyzer is constructed explicitly and must not be discovered by compiler hosts.")]
    [SuppressMessage("MicrosoftCodeAnalysisReleaseTracking",
        "RS2008",
        Justification = "This private instrumentation rule is only used in tests and is not shipped.")]
    private sealed class UnwrapAnalyzer : DiagnosticAnalyzer
    {
        public const string Identifier = "TEST0101";

        private static readonly DiagnosticDescriptor Rule = new(Identifier, title: "Unwrap", messageFormat: "Unwrap", category: "Testing", DiagnosticSeverity.Info, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(static node => node.ReportDiagnostic(Diagnostic.Create(Rule, node.Node.GetFirstToken().GetLocation())), SyntaxKind.IfStatement);
        }
    }

    private sealed class UnwrapFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => [UnwrapAnalyzer.Identifier];

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode? root = await context.Document
                .GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            if (root?.FindToken(context.Span.Start).Parent is not IfStatementSyntax statement)
                return;

            SyntaxNode unwrapped = root.ReplaceNode(statement, statement.Statement.WithLeadingTrivia(statement.GetLeadingTrivia()));

            context.RegisterCodeFix(CodeAction.Create(title: "Unwrap", cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(unwrapped)), equivalenceKey: "Unwrap"),
                context.Diagnostics);
        }
    }
}
