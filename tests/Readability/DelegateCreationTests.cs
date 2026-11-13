using System.Text.RegularExpressions;

using NetAgents.Analyzers.Readability;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Readability;

/// <summary>
/// Covers delegate constructors wrapped around a lambda, anonymous method, or method group, and the fix that assigns
/// what they wrap directly.
/// </summary>
public sealed class DelegateCreationTests
{
    private const string Method = "using System;\n\npublic sealed class ExampleType\n{\n    public event EventHandler Changed;\n\n"
        + "    public Action Callback { get; set; }\n\n    public void Run()\n    {\nBODY\n    }\n\n"
        + "    private static void Invoke(Action action)\n    {\n        action();\n    }\n\n"
        + "    private void OnChanged(object sender, EventArgs arguments)\n    {\n        Changed -= OnChanged;\n    }\n}\n";

    /// <summary>
    /// Verifies the fix unwraps a lambda in a declaration with its type written out, a method group added to an event,
    /// a lambda assigned to a property, and an anonymous method.
    /// </summary>
    /// <param name="written">The statements as written.</param>
    /// <param name="expected">The statements after the fix.</param>
    [Theory]
    [InlineData("EventHandler handler = new((sender, arguments) => Invoke(Callback));\n\nChanged += handler;",
        "EventHandler handler = (sender, arguments) => Invoke(Callback);\n\nChanged += handler;")]
    [InlineData("Changed += new EventHandler(OnChanged);", "Changed += OnChanged;")]
    [InlineData("Callback = new Action(() => Invoke(Callback));", "Callback = () => Invoke(Callback);")]
    [InlineData("Action action = new Action(delegate { Invoke(Callback); });\n\naction();",
        "Action action = delegate { Invoke(Callback); };\n\naction();")]
    public async Task AssignsWhatTheConstructorWraps(string written, string expected)
    {
        Assert.Equal(Wrap(expected), await CodeFixTestHarness.FixDelegateCreations(Wrap(written), fixAll: true));
    }

    /// <summary>
    /// Verifies constructions stay as written where the lambda's own type or an overload could change, as in a var
    /// declaration or an argument, and where the constructor wraps another delegate instance.
    /// </summary>
    /// <param name="statements">A method body that must report nothing.</param>
    [Theory]
    [InlineData("var action = new Action(() => Invoke(Callback));\n\naction();")]
    [InlineData("Invoke(new Action(() => Invoke(Callback)));")]
    [InlineData("Action action = new(Callback);\n\naction();")]
    public async Task KeepsOtherConstructions(string statements)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new DelegateCreationAnalyzer(), Wrap(statements)));
    }

    private static string Wrap(string statements)
    {
        return Method.Replace(oldValue: "BODY",
            Regex.Replace(statements, pattern: "^(?=.)", replacement: "        ", RegexOptions.Multiline),
            StringComparison.Ordinal);
    }
}
