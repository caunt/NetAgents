using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.BuildTasks;

internal sealed class FormattingAnalysis(
    ImmutableArray<DiagnosticAnalyzer> analyzers,
    Action<Exception, DiagnosticAnalyzer, Diagnostic> onAnalyzerException,
    FormattingProgress progress
)
{
    // Keep only the most recent rejection for each action. Error diagnostics retain syntax trees,
    // so retaining every rejected state would grow memory with the number of formatting passes.
    private readonly Dictionary<string, RejectedAction> _rejected = new(StringComparer.Ordinal);

    public static async Task<string> Fingerprint(Project project, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (Document document in project.Documents)
        {
            SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            hash.AppendData(document.Id.Id.ToByteArray());
            hash.AppendData(Encoding.UTF8.GetBytes(text.ChecksumAlgorithm.ToString()));
            hash.AppendData(text.GetChecksum().AsSpan());
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public async Task<ImmutableArray<Diagnostic>> GetDiagnostics(Project project, CancellationToken cancellationToken)
    {
        progress.Report(activity: "analyzing project");

        Compilation compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
            ?? throw new InvalidOperationException(message: "The formatting compilation is unavailable.");

        CompilationWithAnalyzersOptions options = new(project.AnalyzerOptions, onAnalyzerException, concurrentAnalysis: true, logAnalyzerExecutionTime: false);

        return await compilation.WithAnalyzers(analyzers, options).GetAllDiagnosticsAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task<ValidationResult> Validate(Project project, ImmutableArray<Diagnostic> before, string actionKey, bool optional, CancellationToken cancellationToken)
    {
        string? fingerprint = optional ? await Fingerprint(project, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) : null;
        RejectedAction? rejected = null;
        bool cached = fingerprint is not null && _rejected.TryGetValue(actionKey, out rejected) && rejected.Fingerprint == fingerprint;

        if (cached && rejected is not null)
        {
            // Only an identical complete candidate can reuse a rejection: edits in another file can
            // change binding or a project-wide analyzer's result. Compare against today's baseline
            // too, since a previously introduced error might already exist in that baseline now.
            ImmutableArray<Diagnostic> remaining = IntroducedErrors(rejected.Errors, before);

            if (!remaining.IsEmpty)
            {
                progress.Report(activity: "reusing validation of unchanged rejected action");

                return new([], remaining);
            }
        }

        // Suppressors can depend on the complete diagnostic set. When one is present, the full
        // analysis below must establish the effective compiler errors before rejecting a candidate.
        if (!analyzers.Any(static analyzer => analyzer is DiagnosticSuppressor))
        {
            progress.Report(activity: "checking candidate compiler diagnostics");

            Compilation compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
                ?? throw new InvalidOperationException(message: "The formatting compilation is unavailable.");

            ImmutableArray<Diagnostic> compilerDiagnostics = compilation.GetDiagnostics(cancellationToken);
            ImmutableArray<Diagnostic> compilerErrors = IntroducedErrors(compilerDiagnostics, before);

            // A candidate that already breaks compilation cannot be accepted at any severity.
            // Avoid executing all analyzers just to rediscover that.
            if (!compilerErrors.IsEmpty)
            {
                Remember(actionKey, fingerprint, compilerDiagnostics);

                return new([], compilerErrors);
            }
        }

        ImmutableArray<Diagnostic> diagnostics = await GetDiagnostics(project, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        ImmutableArray<Diagnostic> introduced = IntroducedErrors(diagnostics, before);

        if (!introduced.IsEmpty)
            Remember(actionKey, fingerprint, diagnostics);

        return new(diagnostics, introduced);
    }

    private static DiagnosticIdentity ErrorKey(Diagnostic diagnostic)
    {
        return new(diagnostic.Id, diagnostic.GetMessage(CultureInfo.InvariantCulture), diagnostic.Location.SourceTree?.FilePath ?? string.Empty);
    }

    private static ImmutableArray<Diagnostic> IntroducedErrors(ImmutableArray<Diagnostic> after, ImmutableArray<Diagnostic> before)
    {
        Dictionary<DiagnosticIdentity, int> existing = before.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .CountBy(ErrorKey).ToDictionary();

        return [.. after.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .GroupBy(ErrorKey).SelectMany(group => group.Skip(existing.GetValueOrDefault(group.Key)))];
    }

    private void Remember(string actionKey, string? fingerprint, ImmutableArray<Diagnostic> diagnostics)
    {
        if (fingerprint is not null)
            _rejected[actionKey] = new(fingerprint, [.. diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)]);
    }

    private readonly record struct DiagnosticIdentity(string Identifier, string Message, string Path);

    internal sealed record ValidationResult(ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<Diagnostic> IntroducedErrors);

    private sealed record RejectedAction(string Fingerprint, ImmutableArray<Diagnostic> Errors);
}
