using Spectre.Console;
using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    // workspace 布局渲染：把 UiHost 持有的 panels/logs/activeWorkspace 组装成 IRenderable。
    //
    // 无可变 state 所有权——每次渲染时 UiHost 把当前状态打成快照传入。
    // shortcutResolver 把 workspace 映射成"快捷键+标题"显示标签（同 notification popup 用）。
    internal sealed class WorkspaceLayoutBuilder
    {
        static readonly Color[] PluginColors = [Color.DeepSkyBlue1, Color.Green, Color.Yellow, Color.Orange1, Color.MediumPurple, Color.Aqua, Color.Lime];

        // workspace 布局所需的 UiHost 状态快照。shortcutResolver 封装 workspaces→标签查询。
        public readonly record struct State(
            LiveDisplayWorkspace? ActiveWorkspace,
            IReadOnlyCollection<LiveDisplayPanel> Panels,
            IReadOnlyList<LiveDisplayLogLine> Logs,
            Func<LiveDisplayWorkspace, string> ShortcutResolver);

        public IRenderable BuildWorkspaceLayout(in State state, int width, int height)
        {
            if (state.ActiveWorkspace is null)
            {
                var globalLogs = state.Logs
                    .Where(x => x.Workspace is null)
                    .TakeLast(18);
                return BuildBareLogs(globalLogs);
            }

            if (TryGetFullBleedPanel(state.Panels, state.ActiveWorkspace, out var fullBleedPanel))
                return fullBleedPanel.Content;

            var body = width >= 120 && height >= 28
                ? new Layout("Body").SplitColumns(
                    new Layout("Main").Ratio(3),
                    new Layout("Side").Ratio(2))
                : new Layout("Body").SplitRows(
                    new Layout("Main").Ratio(3),
                    new Layout("Side").Ratio(2));

            body["Main"].Update(BuildWorkspacePanels(state.Panels, state.ActiveWorkspace, state.ShortcutResolver));
            body["Side"].Update(BuildSidePanel(state.Logs, state.ActiveWorkspace));
            return body;
        }

        static bool TryGetFullBleedPanel(
            IReadOnlyCollection<LiveDisplayPanel> panels,
            LiveDisplayWorkspace workspace,
            out LiveDisplayPanel panel)
        {
            panel = null!;
            var found = false;
            foreach (var candidate in panels)
            {
                if (candidate.Workspace != workspace || !candidate.FullBleed)
                    continue;

                if (!found || candidate.UpdatedAt >= panel.UpdatedAt)
                {
                    panel = candidate;
                    found = true;
                }
            }

            return found;
        }

        static IRenderable BuildWorkspacePanels(
            IReadOnlyCollection<LiveDisplayPanel> panels,
            LiveDisplayWorkspace workspace,
            Func<LiveDisplayWorkspace, string> shortcutResolver)
        {
            var workspacePanels = panels
                .Where(x => x.Workspace == workspace)
                .OrderBy(x => x.PluginId)
                .ThenBy(x => x.Key)
                .ToList();

            if (workspacePanels.Count == 0)
            {
                return new Panel(new Markup("[grey]当前 workspace 还没有插件输出。[/]"))
                    .Header(shortcutResolver(workspace))
                    .BorderColor(Color.Grey35);
            }

            var renderables = workspacePanels.Select(panel =>
            {
                var renderedPanel = new Panel(panel.Content)
                    .Header($"{panel.PluginId.EscapeMarkup()} - {panel.Title.EscapeMarkup()}")
                    .BorderColor(PluginColor(panel.PluginId));
                return (IRenderable)(workspacePanels.Count == 1 ? renderedPanel.Expand() : renderedPanel);
            }).ToArray();

            return new Rows(renderables);
        }

        static IRenderable BuildSidePanel(IReadOnlyList<LiveDisplayLogLine> logs, LiveDisplayWorkspace workspace)
        {
            var workspaceLogs = logs
                .Where(x => x.Workspace is null || x.Workspace == workspace || x.Severity >= LiveDisplaySeverity.Warning)
                .TakeLast(14);
            return BuildLogPanel(workspaceLogs);
        }

        static IRenderable BuildLogPanel(IEnumerable<LiveDisplayLogLine> source)
        {
            var rows = source.Select(BuildLogRow).ToList();

            if (rows.Count == 0)
                rows.Add(new Markup("[grey]暂无日志。[/]"));

            return new Panel(new Rows([.. rows]))
                .Header("Logs")
                .BorderColor(Color.Grey35);
        }

        // 无 workspace 时，日志直接裸露显示（无面板边框/标题），就像普通控制台输出。
        static IRenderable BuildBareLogs(IEnumerable<LiveDisplayLogLine> source)
        {
            var rows = source.Select(BuildLogRow).ToArray();
            return rows.Length == 0 ? new Markup("") : new Rows(rows);
        }

        static IRenderable BuildLogRow(LiveDisplayLogLine line)
        {
            var prefix = $"[grey]{line.Timestamp:HH:mm:ss}[/] {SeverityMarkup(line.Severity)} [[{line.PluginId.EscapeMarkup()}]] ";
            var fallback = prefix + line.Text.EscapeMarkup();
            return line.IsMarkup
                ? new SafeMarkupRenderable(prefix + line.Text, fallback)
                : new Markup(fallback);
        }

        static string SeverityMarkup(LiveDisplaySeverity severity) => severity switch
        {
            LiveDisplaySeverity.Trace => "[grey]TRACE[/]",
            LiveDisplaySeverity.Info => "[deepskyblue1]INFO[/]",
            LiveDisplaySeverity.Success => "[green]OK[/]",
            LiveDisplaySeverity.Warning => "[yellow]WARN[/]",
            LiveDisplaySeverity.Error => "[red]ERR[/]",
            _ => "[white]INFO[/]"
        };

        static Color PluginColor(string pluginId)
        {
            var hash = (uint)pluginId.GetHashCode();
            return PluginColors[(int)(hash % (uint)PluginColors.Length)];
        }
    }
}
