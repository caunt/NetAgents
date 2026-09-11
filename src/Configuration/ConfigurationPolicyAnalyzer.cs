using System;
using System.Collections.Immutable;
using System.IO;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Configuration;

/// <summary>
/// Requires consumer settings to preserve the embedded configuration policy.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigurationPolicyAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0013";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Consumer configuration must preserve the shared policy",
        messageFormat: "Configuration '{0}' must be '{1}'; remove the conflicting consumer override",
        category: "Configuration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    private static readonly ImmutableDictionary<string, string> RequiredOptions = ReadRequiredOptions();

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterCompilationStartAction(static compilationContext =>
            compilationContext.RegisterSyntaxTreeAction(treeContext => AnalyzeConfiguration(treeContext, compilationContext.Compilation)));
    }

    private static ImmutableDictionary<string, string> ReadRequiredOptions()
    {
        using Stream configurationStream = typeof(ConfigurationPolicyAnalyzer).Assembly.GetManifestResourceStream(
            name: "NetAgents.Analyzers.Configuration.NetAgents.globalconfig")
            ?? throw new InvalidOperationException(message: "The embedded analyzer policy is missing.");

        using StreamReader reader = new(configurationStream);

        ImmutableDictionary<string, string>.Builder options = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            string trimmedLine = line.Trim();

            if (trimmedLine.Length == 0 || trimmedLine.StartsWith(value: "#", StringComparison.Ordinal))
                continue;

            int separatorIndex = trimmedLine.IndexOf(value: '=');

            if (separatorIndex < 0)
                continue;

            string key = trimmedLine.Substring(startIndex: 0, length: separatorIndex).Trim();
            string value = trimmedLine.Substring(separatorIndex + 1).Trim();

            if (key is not "is_global" and not "global_level" && value.Length > 0)
                options[key] = value;
        }

        return options.ToImmutable();
    }

    private static void AnalyzeConfiguration(SyntaxTreeAnalysisContext context, Compilation compilation)
    {
        AnalyzerConfigOptions options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree);

        foreach (System.Collections.Generic.KeyValuePair<string, string> requiredOption in RequiredOptions)
        {
            bool matches;

            bool isDiagnosticSeverity = requiredOption.Key.StartsWith(value: "dotnet_diagnostic.", StringComparison.OrdinalIgnoreCase)
                && requiredOption.Key.EndsWith(value: ".severity", StringComparison.OrdinalIgnoreCase);

            if (isDiagnosticSeverity)
            {
                string diagnosticIdentifier = requiredOption.Key.Substring(startIndex: 18,
                    length: requiredOption.Key.Length - 18 - 9);

                ReportDiagnostic expectedSeverity = requiredOption.Value switch
                {
                    "error" => ReportDiagnostic.Error,
                    "warning" => ReportDiagnostic.Warn,
                    "suggestion" => ReportDiagnostic.Info,
                    "silent" => ReportDiagnostic.Hidden,
                    "none" => ReportDiagnostic.Suppress,
                    _ => ReportDiagnostic.Default,
                };

                SyntaxTreeOptionsProvider? provider = compilation.Options.SyntaxTreeOptionsProvider;
                matches = provider is not null
                    && (provider.TryGetDiagnosticValue(context.Tree, diagnosticIdentifier, context.CancellationToken, out ReportDiagnostic severity)
                        ? severity == expectedSeverity
                        : provider.TryGetGlobalDiagnosticValue(diagnosticIdentifier, context.CancellationToken, out severity)
                            && severity == expectedSeverity);
            }
            else
            {
                matches = options.TryGetValue(requiredOption.Key, out string? actualValue)
                    && string.Equals(actualValue, requiredOption.Value, StringComparison.OrdinalIgnoreCase);
            }

            if (!matches)
            {
                string[] messageArguments = [requiredOption.Key, requiredOption.Value];
                context.ReportDiagnostic(Diagnostic.Create(Rule,
                    Location.Create(context.Tree, new TextSpan(start: 0, length: 0)), messageArgs: messageArguments));
            }
        }
    }
}
