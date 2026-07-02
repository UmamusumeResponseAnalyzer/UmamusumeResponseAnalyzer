using System.Globalization;
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
            var prefix = $"{SeverityMarkup(line.Severity)} [[{line.PluginId.EscapeMarkup()}]]";
            return new LogRowRenderable(prefix, line.Text, line.IsMarkup);
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

        sealed class LogRowRenderable(string prefixMarkup, string text, bool isMarkup) : IRenderable
        {
            public Measurement Measure(RenderOptions options, int maxWidth)
                => ((IRenderable)new Markup($"{prefixMarkup} {text.EscapeMarkup()}")).Measure(options, maxWidth);

            public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
            {
                if (maxWidth <= 0)
                    yield break;

                var prefixSegments = ((IRenderable)new Markup(prefixMarkup)).Render(options, maxWidth).ToArray();
                var messageLines = BuildMessageLines(options, maxWidth);
                if (messageLines.Count == 0)
                    messageLines.Add(Array.Empty<Segment>());

                var renderedAnyLine = false;
                for (var i = 0; i < messageLines.Count; i++)
                {
                    IReadOnlyList<Segment> line = i == 0
                        ? [.. prefixSegments, new Segment(" "), .. messageLines[i]]
                        : messageLines[i];
                    foreach (var segment in WrapLine(line, maxWidth, renderedAnyLine))
                    {
                        renderedAnyLine = true;
                        yield return segment;
                    }
                }
            }

            List<IReadOnlyList<Segment>> BuildMessageLines(RenderOptions options, int maxWidth)
            {
                var segments = isMarkup
                    ? RenderMarkupMessage(options, Math.Max(maxWidth, text.GetCellWidth()))
                    : RenderPlainMessage();
                var lines = new List<IReadOnlyList<Segment>> { Array.Empty<Segment>() };

                foreach (var segment in segments)
                {
                    if (segment.IsLineBreak)
                    {
                        lines.Add(Array.Empty<Segment>());
                        continue;
                    }

                    lines[^1] = [.. lines[^1], segment];
                }

                return lines;
            }

            IEnumerable<Segment> RenderMarkupMessage(RenderOptions options, int maxWidth)
            {
                try
                {
                    return ((IRenderable)new Markup(text)).Render(options, maxWidth).ToArray();
                }
                catch
                {
                    return RenderPlainMessage();
                }
            }

            IEnumerable<Segment> RenderPlainMessage()
            {
                var first = true;
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                {
                    if (!first)
                        yield return Segment.LineBreak;

                    yield return new Segment(line);
                    first = false;
                }
            }

            static IEnumerable<Segment> WrapLine(IReadOnlyList<Segment> line, int maxWidth, bool prependLineBreak)
            {
                var currentWidth = 0;
                var started = false;
                foreach (var segment in line)
                {
                    if (segment.IsLineBreak || segment.IsControlCode)
                        continue;

                    foreach (var element in EnumerateTextElements(segment.Text))
                    {
                        if (!started)
                        {
                            if (prependLineBreak)
                                yield return Segment.LineBreak;
                            started = true;
                        }

                        if (currentWidth > 0 && currentWidth + element.CellWidth > maxWidth)
                        {
                            yield return Segment.LineBreak;
                            currentWidth = 0;
                        }

                        yield return new Segment(element.Text, segment.Style);
                        currentWidth += element.CellWidth;
                    }
                }

                if (!started && prependLineBreak)
                    yield return Segment.LineBreak;
            }

            static IEnumerable<TextElement> EnumerateTextElements(string value)
            {
                var indexes = StringInfo.ParseCombiningCharacters(value);
                for (var i = 0; i < indexes.Length; i++)
                {
                    var start = indexes[i];
                    var end = i + 1 < indexes.Length ? indexes[i + 1] : value.Length;
                    var text = value[start..end];
                    yield return new TextElement(text, text.GetCellWidth());
                }
            }

            readonly record struct TextElement(string Text, int CellWidth);
        }
    }
}
