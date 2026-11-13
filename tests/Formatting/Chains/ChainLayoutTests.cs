using System.Text.RegularExpressions;

using NetAgents.Analyzers.Formatting.Chains;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Formatting.Chains;

/// <summary>
/// Covers the layout NETAGENTS0047 gives member chains, the chains it leaves as written, and the fix.
/// </summary>
public sealed class ChainLayoutTests
{
    private const string Method = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing System.Threading.Tasks;\n\n"
        + "public sealed class Node\n{\n    public Node Next = null!;\n\n    public int Depth;\n}\n\n"
        + "public sealed class ExampleType\n{\n    public async Task<int> Run(int[] values, Dictionary<string, int[]> rows, Node node)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies chains stay as written when they already fit, hold a single link, or sit where another rule or the
    /// author decides their lines: a condition, an interpolation, a comment beside a dot, a <c>?.</c> access, or an
    /// argument list NETAGENTS0020 writes on one line, even when its line passes 128 characters. Namespace and type
    /// qualifiers do not count as links.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("return values.Where(static value => value > 0).Select(static value => value * 2).Sum();")]
    [InlineData("return node.Next.Next.Next.Depth;")]
    [InlineData("int total = values\n    .Sum();\n\nreturn total;")]
    [InlineData("int count = values\n    .Length;\n\nreturn count;")]
    [InlineData("return System.Linq.Enumerable.Range(start: 0, count: 3).Select(static value => value * 2).Sum();")]
    [InlineData("if (values.Where(static value => value > 0).Select(static value => value * 2).Distinct().Order().Any())\n    return 1;\n\nreturn 0;")]
    [InlineData("string text = $\"{values.Where(static value => value > 0).Select(static value => value * 2).Distinct().Order().Sum()}\";\n\nreturn text.Length;")]
    [InlineData("return values // Only positive values count.\n    .Where(static value => value > 0).Select(static value => value * 2).Sum();")]
    [InlineData("return rows.Values.FirstOrDefault()?.Where(static value => value > 0).Select(static value => value * 2).Distinct().Order().Sum() ?? 0;")]
    [InlineData("Console.WriteLine(values.Where(static value => value > 0).Select(static value => value * 2).Distinct().Order().Sum());\n\nreturn 0;")]
    [InlineData("Console.WriteLine(rows.Values.Where(static cells => cells.Length > 1).Select(static cells => cells.Length * 2).Sum() + values.Length);\n\nreturn 0;")]
    public async Task KeepsChainsAsWritten(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new ChainLayoutAnalyzer(), Wrap(statements)));
    }

    /// <summary>
    /// Verifies the fix joins a short chain and splits one that does not fit or holds more than four links before
    /// every link after the first, whether it is a field, a property, or a call, while the first link and namespace
    /// and type qualifiers stay with the receiver.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("await Console\n    .Error.WriteLineAsync(\"Failed\")\n    .ConfigureAwait(continueOnCapturedContext: false);\n\nreturn 0;",
        "await Console.Error.WriteLineAsync(\"Failed\").ConfigureAwait(continueOnCapturedContext: false);\n\nreturn 0;")]
    [InlineData("return values.Where(static value => value > 0 && value < 1000 && value % 2 == 0 && value % 3 == 0).Select(static value => value * 2).Sum();",
        "return values.Where(static value => value > 0 && value < 1000 && value % 2 == 0 && value % 3 == 0)\n    .Select(static value => value * 2)\n    .Sum();")]
    [InlineData("return values.Where(static value => value > 0).Select(static value => value * 2).Distinct().Order().Sum();",
        "return values.Where(static value => value > 0)\n    .Select(static value => value * 2)\n    .Distinct()\n    .Order()\n    .Sum();")]
    [InlineData("return rows.Values.Where(static cells => cells.Length > 0).Select(static cells => cells.Length).ToArray().Length;",
        "return rows.Values\n    .Where(static cells => cells.Length > 0)\n    .Select(static cells => cells.Length)\n    .ToArray()\n    .Length;")]
    [InlineData("int count = rows\n    .Values\n    .Where(static cells => cells.Length > 0)\n    .Select(static cells => cells.Length)\n    .Distinct()\n    .Count();\n\nreturn count;",
        "int count = rows.Values\n    .Where(static cells => cells.Length > 0)\n    .Select(static cells => cells.Length)\n    .Distinct()\n    .Count();\n\nreturn count;")]
    [InlineData("return node.Next.Next.Next.Next.Depth;", "return node.Next\n    .Next\n    .Next\n    .Next\n    .Depth;")]
    [InlineData("int length = Console\n    .Out\n    .Encoding\n    .EncodingName\n    .Length;\n\nreturn length;",
        "int length = Console.Out.Encoding.EncodingName.Length;\n\nreturn length;")]
    [InlineData("int length = System.Console.Out.Encoding.EncodingName.Trim().Length;\n\nreturn length;",
        "int length = System.Console.Out\n    .Encoding\n    .EncodingName\n    .Trim()\n    .Length;\n\nreturn length;")]
    public async Task LaysOutChains(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixChains(Wrap(written), fixAll: true));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
