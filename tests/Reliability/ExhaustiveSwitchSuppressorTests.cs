using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Reliability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Reliability;

/// <summary>Checks that populate-switch suggestions survive only where a default arm is missing.</summary>
public sealed class ExhaustiveSwitchSuppressorTests
{
    private const string Enumeration = "enum Width { Int32, SignedByte, VarInt }\n";

    /// <summary>Keeps the suggestion when the switch leaves a member genuinely unhandled.</summary>
    /// <param name="body">The switch the suggestion describes.</param>
    /// <param name="diagnosticIdentifier">The populate-switch rule reported for that switch.</param>
    [Theory]
    [InlineData("int Read(Width width) { switch (width) { case Width.Int32: return 4; } return 0; }", "IDE0010")]
    [InlineData("int Read(Width width) => width switch { Width.Int32 => 4, Width.SignedByte => 1 };", "IDE0072")]
    [InlineData("int Read(Width width) => width switch { Width.Int32 => 4, _ when width == Width.VarInt => 1 };", "IDE0072")]
    public async Task ReportsSwitchesWithoutDefaultArm(string body, string diagnosticIdentifier)
    {
        Diagnostic diagnostic = await Analyze(body, diagnosticIdentifier);

        Assert.False(diagnostic.IsSuppressed);
    }

    /// <summary>Suppresses the suggestion for every arm shape that already handles the rest.</summary>
    /// <param name="body">The switch the suggestion describes.</param>
    /// <param name="diagnosticIdentifier">The populate-switch rule reported for that switch.</param>
    [Theory]
    [InlineData("int Read(Width width) { switch (width) { case Width.Int32: return 4; default: return 0; } }", "IDE0010")]
    [InlineData("int Read(Width width) => width switch { Width.Int32 => 4, _ => 0 };", "IDE0072")]
    [InlineData("int Read(Width width) => width switch { Width.Int32 => 4, var other => 0 };", "IDE0072")]
    public async Task SuppressesSwitchesWithDefaultArm(string body, string diagnosticIdentifier)
    {
        Diagnostic diagnostic = await Analyze(body, diagnosticIdentifier);

        Assert.True(diagnostic.IsSuppressed);
    }

    /// <summary>Reads the arms of the reported switch rather than an enclosing one.</summary>
    [Fact]
    public async Task SuppressesTheReportedSwitchOnly()
    {
        // The statement has no default and the expression inside it does, so exactly one survives.
        const string body = "int Read(Width width) { switch (width) { case Width.Int32: return width switch { Width.Int32 => 4, _ => 0 }; } return 0; }";
        Diagnostic statement = await Analyze(body, diagnosticIdentifier: "IDE0010");
        Diagnostic expression = await Analyze(body, diagnosticIdentifier: "IDE0072");

        Assert.False(statement.IsSuppressed);
        Assert.True(expression.IsSuppressed);
    }

    /// <summary>Suppresses the suggestion even where configuration raises it to an error.</summary>
    /// <param name="severity">The severity the analyzer configuration assigns.</param>
    [Theory]
    [InlineData("error")]
    [InlineData("warning")]
    public async Task SuppressesWhileConfigurationRaisesSeverity(string severity)
    {
        const string body = "int Read(Width width) => width switch { Width.Int32 => 4, _ => 0 };";
        Diagnostic diagnostic = await Analyze(body, diagnosticIdentifier: "IDE0072", severity);

        Assert.True(diagnostic.IsSuppressed);
    }

    private static async Task<Diagnostic> Analyze(string body, string diagnosticIdentifier, string? severity = null)
    {
        using AdhocWorkspace workspace = new();

        // The populate-switch analyzers ship with the SDK rather than this package, so the shape of
        // their report is reproduced here: the rule identifier, on the switch keyword.
        Project project = workspace.AddProject(name: "Switches", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, generalDiagnosticOption: ReportDiagnostic.Error))
            .AddMetadataReferences(AnalyzerTestHarness.References)
            .AddDocument(name: "Switches.cs", SourceText.From(Enumeration + "class Switches { " + body + " }\n"), filePath: "Switches.cs").Project;

        if (severity is not null)
        {
            project = project.AddAnalyzerConfigDocument(
                name: ".globalconfig",
                SourceText.From($"is_global = true\ndotnet_diagnostic.{diagnosticIdentifier}.severity = {severity}\n"),
                filePath: "/.globalconfig"
            ).Project;
        }

        Compilation compilation = await project.GetCompilationAsync().ConfigureAwait(continueOnCapturedContext: false)
            ?? throw new InvalidOperationException(message: "The compilation is unavailable.");

        // Suppressed diagnostics are reported so a suppression is distinguishable from no report at all.
        CompilationWithAnalyzersOptions options = new(
            project.AnalyzerOptions,
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: true
        );

        CompilationWithAnalyzers analysis = compilation.WithAnalyzers([new PopulateSwitchAnalyzer(diagnosticIdentifier), new ExhaustiveSwitchSuppressor()], options);

        ImmutableArray<Diagnostic> diagnostics = await analysis.GetAllDiagnosticsAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

        return Assert.Single(diagnostics.Where(candidate => candidate.Id == diagnosticIdentifier));
    }

    /// <summary>Reports a populate-switch suggestion the way the SDK's own analyzers report one.</summary>
    /// <param name="identifier">The populate-switch rule to report.</param>
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
    private sealed class PopulateSwitchAnalyzer(string identifier) : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor ExpressionRule = new(
            id: "IDE0072",
            title: "Add missing cases",
            messageFormat: "Add missing cases",
            category: "Style",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor StatementRule = new(
            id: "IDE0010",
            title: "Add missing cases",
            messageFormat: "Add missing cases",
            category: "Style",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

        private readonly DiagnosticDescriptor _rule = identifier == "IDE0010" ? StatementRule : ExpressionRule;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [_rule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(Report, identifier == "IDE0010" ? SyntaxKind.SwitchStatement : SyntaxKind.SwitchExpression);
        }

        private void Report(SyntaxNodeAnalysisContext context)
        {
            SyntaxToken keyword = context.Node is SwitchStatementSyntax statement
                ? statement.SwitchKeyword
                : ((SwitchExpressionSyntax)context.Node).SwitchKeyword;

            context.ReportDiagnostic(Diagnostic.Create(_rule, keyword.GetLocation()));
        }
    }
}
