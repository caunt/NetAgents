using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace NetAgents.BuildTasks;

internal sealed class BuildDiagnostics(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
{
    public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<Diagnostic>>([.. diagnostics]);
    }

    public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
    {
        SyntaxTree? tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return diagnostics.Where(diagnostic => diagnostic.Location.SourceTree == tree);
    }

    public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
    {
        return Task.FromResult(diagnostics.Where(static diagnostic => !diagnostic.Location.IsInSource));
    }
}
