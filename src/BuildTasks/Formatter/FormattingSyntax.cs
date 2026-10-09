using Microsoft.CodeAnalysis;

namespace NetAgents.BuildTasks;

internal static class FormattingSyntax
{
    public static void Verify(IEnumerable<Diagnostic> diagnostics)
    {
        string[] errors = [.. diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())];

        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                "Automatic formatting stopped because source contains syntax errors."
                + Environment.NewLine + string.Join(Environment.NewLine, errors)
            );
        }
    }

    public static async Task Verify(Project project, CancellationToken cancellationToken)
    {
        List<Diagnostic> diagnostics = [];

        foreach (Document document in project.Documents)
        {
            SyntaxTree tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
                ?? throw new InvalidOperationException(message: "A formatting document has no syntax tree.");

            diagnostics.AddRange(tree.GetDiagnostics(cancellationToken));
        }

        Verify(diagnostics);
    }
}
