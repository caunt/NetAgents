using System.Globalization;

using Microsoft.CodeAnalysis;

using NetAgents.Analyzers.Formatting.Chains;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting.Chains;

/// <summary>
/// Covers runs of property and field accesses, which NETAGENTS0048 limits to four in a row.
/// </summary>
public sealed class MemberPathDepthTests
{
    private const string Source = "public sealed class Node\n{\n    public Node Next => this;\n\n    public Node[] Children => [this];\n\n"
        + "    public int Depth => 0;\n\n    public Node Copy()\n    {\n        return this;\n    }\n}\n\n"
        + "public static class ExampleType\n{\n    public static int Read(Node node)\n    {\n        return PATH;\n    }\n}\n";

    /// <summary>
    /// Verifies paths of up to four accesses pass, a call starts the count again, and namespace and type qualifiers
    /// are part of the receiver.
    /// </summary>
    /// <param name="path">The expression the method returns.</param>
    [Theory]
    [InlineData("node.Next.Next.Next.Depth")]
    [InlineData("node.Next.Next.Next.Next.Copy().Next.Next.Next.Depth")]
    [InlineData("System.Text.Encoding.UTF8.WebName.Length")]
    public async Task AcceptsShortPaths(string path)
    {
        Assert.Empty(await Analyze(path));
    }

    /// <summary>
    /// Verifies a fifth access in a row is reported with the depth, an element access included in the run.
    /// </summary>
    /// <param name="path">The expression the method returns.</param>
    /// <param name="depth">The depth the message reports.</param>
    [Theory]
    [InlineData("node.Next.Next.Next.Next.Depth", "5")]
    [InlineData("node.Copy().Children[0].Next.Next.Next.Next.Depth", "6")]
    public async Task RejectsDeepPaths(string path, string depth)
    {
        Diagnostic diagnostic = Assert.Single(await Analyze(path));

        Assert.Equal(MemberPathDepthAnalyzer.RuleIdentifier, diagnostic.Id);
        Assert.Contains($"it goes {depth} members deep", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static Task<Diagnostic[]> Analyze(string path)
    {
        return AnalyzerTestHarness.Analyze(new MemberPathDepthAnalyzer(), Source.Replace(oldValue: "PATH", path, StringComparison.Ordinal));
    }
}
