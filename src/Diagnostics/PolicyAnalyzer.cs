using System;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.Analyzers.Diagnostics;

/// <summary>
/// Applies shared initialization and mandatory diagnostic settings to every policy analyzer.
/// </summary>
/// <param name="rule">The diagnostic reported by the analyzer.</param>
/// <param name="analyzesRazorCode">
/// Whether the rule also checks the C# statements, members, and lambdas written in Razor components and
/// views, which reach the compiler only inside the code Razor generates for them.
/// </param>
public abstract class PolicyAnalyzer(DiagnosticDescriptor rule, bool analyzesRazorCode = false) : DiagnosticAnalyzer
{
    private readonly ImmutableArray<DiagnosticDescriptor> _supportedDiagnostics = [ValidateRule(rule)];

    /// <inheritdoc />
    public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => _supportedDiagnostics;

    /// <inheritdoc />
    public sealed override void Initialize(AnalysisContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        // Razor's output is generated code with its author's C# copied into it, so a rule that checks
        // that C# reads generated trees, and its context keeps each callback to what the author wrote.
        GeneratedCodeAnalysisFlags generatedCode = analyzesRazorCode
            ? GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics
            : GeneratedCodeAnalysisFlags.None;

        context.ConfigureGeneratedCodeAnalysis(generatedCode);
        context.EnableConcurrentExecution();
        RegisterAnalysisActions(analyzesRazorCode ? new RazorAnalysisContext(context) : context);
    }

    /// <summary>
    /// Registers the callbacks that implement the analyzer's rule after common initialization.
    /// </summary>
    /// <param name="context">The context receiving this analyzer's callbacks.</param>
    protected abstract void RegisterAnalysisActions(AnalysisContext context);

    private static DiagnosticDescriptor ValidateRule(DiagnosticDescriptor rule)
    {
        if (rule is null)
            throw new ArgumentNullException(nameof(rule));

        bool isMandatoryError = rule.DefaultSeverity == DiagnosticSeverity.Error && rule.IsEnabledByDefault
            && rule.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable, StringComparer.Ordinal);

        return !isMandatoryError
            ? throw new ArgumentException(message: "Policy diagnostics must be enabled, non-configurable errors.", nameof(rule))
            : rule;
    }
}
