using System.Text.RegularExpressions;

using NetAgents.Analyzers.Readability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

/// <summary>
/// Covers a try statement that only adds a finally to the try it wraps, and the fix that merges the two.
/// </summary>
public sealed class NestedTryTests
{
    private const string Method = "using System;\nusing System.IO;\n\npublic static class ExampleType\n{\n"
        + "    public static void Run(Action work, Action<Exception> log, Action release)\n    {\nBODY\n    }\n}\n";

    /// <summary>
    /// Verifies nestings that would change what runs when stay as written: an outer try with handlers, an inner try
    /// with its own finally, an outer block holding more than the inner try, and a comment between the two.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("try\n{\n    try\n    {\n        work();\n    }\n    finally\n    {\n        release();\n    }\n}\ncatch (IOException exception)\n{\n    log(exception);\n}")]
    [InlineData("try\n{\n    try\n    {\n        work();\n    }\n    catch (IOException exception)\n    {\n        log(exception);\n    }\n    finally\n    {\n        release();\n    }\n}\nfinally\n{\n    release();\n}")]
    [InlineData("try\n{\n    work();\n\n    try\n    {\n        work();\n    }\n    catch (IOException exception)\n    {\n        log(exception);\n    }\n}\nfinally\n{\n    release();\n}")]
    [InlineData("try\n{\n    // The work may fail on disk.\n    try\n    {\n        work();\n    }\n    catch (IOException exception)\n    {\n        log(exception);\n    }\n}\nfinally\n{\n    release();\n}")]
    [InlineData("try\n{\n    work();\n}\ncatch (IOException exception)\n{\n    log(exception);\n}\nfinally\n{\n    release();\n}")]
    public async Task KeepsOtherNestings(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new NestedTryAnalyzer(), Wrap(statements)));
    }

    /// <summary>
    /// Verifies the inner block and handlers move into the outer statement, which keeps its finally, and the formatter
    /// indents them one level shallower.
    /// </summary>
    [Fact]
    public async Task MergesTryThatOnlyAddsAFinally()
    {
        const string written = "try\n{\n    try\n    {\n        work();\n    }\n    catch (IOException exception) when (exception.HResult != 0)\n    {\n        log(exception);\n    }\n"
            + "    catch (InvalidOperationException exception)\n    {\n        log(exception);\n    }\n}\nfinally\n{\n    release();\n}";

        const string expected = "try\n{\n    work();\n}\ncatch (IOException exception) when (exception.HResult != 0)\n{\n    log(exception);\n}\n"
            + "catch (InvalidOperationException exception)\n{\n    log(exception);\n}\nfinally\n{\n    release();\n}";

        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixNestedTries(Wrap(written), fixAll: true));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
