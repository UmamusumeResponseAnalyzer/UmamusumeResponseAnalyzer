using Spectre.Console;
using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    internal sealed class BootstrapWorkspace
    {
        const string HostSource = "URA";

        readonly UiHost uiHost;
        readonly object gate = new();
        readonly List<(string Label, string Value)> settings = [];
        readonly Dictionary<string, Phase> phases = [];

        public BootstrapWorkspace(UiHost uiHost)
        {
            this.uiHost = uiHost;
            Workspace = uiHost.CreateWorkspace("bootstrap", "启动");
            uiHost.BindWorkspaceHotkey(Workspace, ConsoleKey.B, ConsoleModifiers.Control, "启动信息");
            Refresh();
        }

        public LiveDisplayWorkspace Workspace { get; }

        public void SetSettings(IReadOnlyList<(string Label, string Value)> values)
        {
            lock (gate)
            {
                settings.Clear();
                settings.AddRange(values);
                RefreshLocked();
            }
        }

        public void SetPhase(string key, string label, LiveDisplaySeverity severity, string detail)
        {
            lock (gate)
            {
                phases[key] = new(label, severity, detail);
                RefreshLocked();
            }
        }

        public void Log(string source, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info)
        {
            uiHost.Log(new LiveDisplayLogLine(Workspace, source, text, severity, IsMarkup: false, DateTimeOffset.Now));
        }

        public void MarkupLog(string source, string markup, LiveDisplaySeverity severity = LiveDisplaySeverity.Info)
        {
            uiHost.Log(new LiveDisplayLogLine(Workspace, source, markup, severity, IsMarkup: true, DateTimeOffset.Now));
        }

        void Refresh()
        {
            lock (gate)
                RefreshLocked();
        }

        void RefreshLocked()
        {
            uiHost.SetPanel(new LiveDisplayPanel(
                Workspace,
                HostSource,
                "status",
                "启动状态",
                BuildContent(),
                DateTimeOffset.Now));
        }

        IRenderable BuildContent()
        {
            var rows = new List<IRenderable>
            {
                new Markup("[bold]设置[/]")
            };

            if (settings.Count == 0)
            {
                rows.Add(new Markup("[grey]尚未读取配置。[/]"));
            }
            else
            {
                foreach (var (label, value) in settings)
                    rows.Add(new Markup($"[grey]{label.EscapeMarkup()}[/] {value.EscapeMarkup()}"));
            }

            rows.Add(new Text(string.Empty));
            rows.Add(new Markup("[bold]启动阶段[/]"));

            if (phases.Count == 0)
            {
                rows.Add(new Markup("[grey]等待启动。[/]"));
            }
            else
            {
                foreach (var phase in phases.Values)
                    rows.Add(new Markup($"{SeverityMarkup(phase.Severity)} [grey]{phase.Label.EscapeMarkup()}[/] {phase.Detail.EscapeMarkup()}"));
            }

            return new Rows(rows);
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

        sealed record Phase(string Label, LiveDisplaySeverity Severity, string Detail);
    }
}
