using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace NetAgents.Analyzers.Tests.Infrastructure;

internal sealed class SpacingDiagnosticProvider : FixAllContext.DiagnosticProvider
{
    public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
    {
        return (await CodeFixTestHarness.GetSpacingDiagnostics(document).ConfigureAwait(continueOnCapturedContext: false)).ToArray();
    }

    public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<Diagnostic>>(Array.Empty<Diagnostic>());
    }

    public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
    {
        List<Diagnostic> diagnostics = [];

        foreach (Document document in project.Documents)
        {
            diagnostics.AddRange(await GetDocumentDiagnosticsAsync(document, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));
        }

        return diagnostics;
    }
}
