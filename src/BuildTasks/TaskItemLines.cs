using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace NetAgents.BuildTasks;

/// <summary>
/// Reads the item lists MSBuild hands the formatting task as text, one item per line.
/// </summary>
/// <remarks>
/// MSBuild binds item lists only to array parameters, which CA1819 rejects on a public type, so the targets flatten
/// each list with a transform such as <c>@(Compile->'%(FullPath)%09%(Link)', '%0A')</c>: a line per item holding its
/// full path, then a tab before each metadata value the formatter reads, in a fixed order. Neither a line break nor
/// a tab can appear in a path.
/// </remarks>
internal static class TaskItemLines
{
    public static IReadOnlyList<ITaskItem> Parse(string lines, params string[] metadataNames)
    {
        List<ITaskItem> items = [];

        foreach (string line in lines.Split(separator: '\n'))
        {
            string[] fields = line.TrimEnd(trimChar: '\r').Split(separator: '\t');

            if (fields[0].Length == 0)
                continue;

            // A TaskItem unescapes its item spec, so a literal percent sign in a path is escaped before it gets there.
            TaskItem item = new(fields[0].Replace(oldValue: "%", newValue: "%25", StringComparison.Ordinal));

            for (int index = 0; index < metadataNames.Length && index + 1 < fields.Length; index++)
            {
                if (fields[index + 1].Length > 0)
                    item.SetMetadata(metadataNames[index], fields[index + 1]);
            }

            items.Add(item);
        }

        return items;
    }
}
