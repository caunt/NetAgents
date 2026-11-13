using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NetAgents.Analyzers.Tests.Infrastructure;

// Tests compare a document's text with the source they expect, which is all most of them read from a document.
internal static class DocumentText
{
    public static async Task<string> Read(Document document)
    {
        SourceText text = await document.GetTextAsync().ConfigureAwait(continueOnCapturedContext: false);

        return text.ToString();
    }
}
