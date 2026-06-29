using Spectre.Console;
using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    public sealed record LiveDisplayPanel(
        LiveDisplayWorkspace Workspace,
        string PluginId,
        string Key,
        string Title,
        IRenderable Content,
        DateTimeOffset UpdatedAt,
        bool FullBleed = false);

    public sealed record LiveDisplayLogLine(
        LiveDisplayWorkspace? Workspace,
        string PluginId,
        string Text,
        LiveDisplaySeverity Severity,
        bool IsMarkup,
        DateTimeOffset Timestamp);

    public sealed record LiveDisplayNotification(
        LiveDisplayWorkspace? Workspace,
        string PluginId,
        string Text,
        LiveDisplaySeverity Severity,
        DateTimeOffset ExpiresAt)
    {
        internal static TimeSpan DefaultTtl(LiveDisplaySeverity severity)
        {
            return severity >= LiveDisplaySeverity.Warning
                ? TimeSpan.FromSeconds(10)
                : TimeSpan.FromSeconds(5);
        }

        internal static DateTimeOffset ExpiresAtFromNow(LiveDisplaySeverity severity, TimeSpan? ttl = null)
        {
            return DateTimeOffset.Now.Add(ttl ?? DefaultTtl(severity));
        }
    }

    public interface ILiveDisplayOutput
    {
        LiveDisplayWorkspace CreateWorkspace(string id, string title);
        void SwitchWorkspace(LiveDisplayWorkspace workspace);
        void BindWorkspaceHotkey(LiveDisplayWorkspace workspace, ConsoleKey key, ConsoleModifiers modifiers = 0, string? description = null);
        void SetPanel(LiveDisplayWorkspace workspace, string key, string title, IRenderable content, bool fullBleed = false);
        void Log(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info);
        void MarkupLog(LiveDisplayWorkspace workspace, string markup, LiveDisplaySeverity severity = LiveDisplaySeverity.Info);
        void Notify(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info, TimeSpan? ttl = null);
    }

    public sealed class PluginLiveDisplayOutput : ILiveDisplayOutput
    {
        readonly string pluginId;
        readonly string workspaceIdPrefix;
        readonly UiHost uiHost;

        public PluginLiveDisplayOutput(string pluginId, UiHost uiHost)
        {
            this.pluginId = NormalizeComponent(pluginId, nameof(pluginId));
            workspaceIdPrefix = $"{this.pluginId.Length}:{this.pluginId}:";
            this.uiHost = uiHost;
        }

        public LiveDisplayWorkspace CreateWorkspace(string id, string title)
        {
            return uiHost.CreateWorkspace(ComposeWorkspaceId(id), title);
        }

        public void SwitchWorkspace(LiveDisplayWorkspace workspace)
        {
            uiHost.SwitchWorkspace(workspace);
        }

        public void BindWorkspaceHotkey(LiveDisplayWorkspace workspace, ConsoleKey key, ConsoleModifiers modifiers = 0, string? description = null)
        {
            uiHost.BindWorkspaceHotkey(
                workspace,
                key,
                modifiers,
                description ?? $"切换到 {workspace.Title}");
        }

        public void SetPanel(LiveDisplayWorkspace workspace, string key, string title, IRenderable content, bool fullBleed = false)
            => uiHost.SetPanel(new LiveDisplayPanel(workspace, pluginId, key, title, content, DateTimeOffset.Now, fullBleed));

        public void Log(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info)
            => uiHost.Log(new LiveDisplayLogLine(workspace, pluginId, text, severity, IsMarkup: false, DateTimeOffset.Now));

        public void MarkupLog(LiveDisplayWorkspace workspace, string markup, LiveDisplaySeverity severity = LiveDisplaySeverity.Info)
            => uiHost.Log(new LiveDisplayLogLine(workspace, pluginId, markup, severity, IsMarkup: true, DateTimeOffset.Now));

        public void Notify(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info, TimeSpan? ttl = null)
            => uiHost.Notify(new LiveDisplayNotification(workspace, pluginId, text, severity, LiveDisplayNotification.ExpiresAtFromNow(severity, ttl)));

        string ComposeWorkspaceId(string id)
        {
            var normalizedId = NormalizeComponent(id, nameof(id));
            return workspaceIdPrefix + normalizedId;
        }

        static string NormalizeComponent(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Plugin/workspace id 不能为空。", parameterName);

            return value.Trim();
        }
    }
}
