using System.Diagnostics;
using System.Globalization;

namespace NetAgents.BuildTasks;

internal sealed class FormattingProgress(string projectName, Action<string>? report)
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private int _pass;

    public void NextPass()
    {
        _pass++;
        Report(activity: "checking available fixes");
    }

    public void Report(string activity)
    {
        if (report is null)
            return;

        string seconds = _elapsed.Elapsed.TotalSeconds.ToString(format: "F1", CultureInfo.InvariantCulture);
        string pass = _pass.ToString(CultureInfo.InvariantCulture);
        report($"NetAgents formatting {projectName}: pass {pass}, elapsed {seconds}s — {activity}");
    }
}
