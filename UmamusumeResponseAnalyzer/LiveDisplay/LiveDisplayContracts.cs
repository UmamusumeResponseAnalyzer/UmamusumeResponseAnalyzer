using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay;

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

public sealed class LiveDisplayWorkspace : IEquatable<LiveDisplayWorkspace>
{
    static readonly StringComparer IdComparer = StringComparer.Ordinal;

    LiveDisplayWorkspace(string id, string title)
    {
        Id = Normalize(id, nameof(id));
        Title = Normalize(title, nameof(title));
    }

    public string Id { get; }
    public string Title { get; }

    public static LiveDisplayWorkspace Create(string id, string title)
    {
        return new LiveDisplayWorkspace(id, title);
    }

    public bool Equals(LiveDisplayWorkspace? other)
    {
        return other is not null && IdComparer.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj)
    {
        return obj is LiveDisplayWorkspace other && Equals(other);
    }

    public override int GetHashCode()
    {
        return IdComparer.GetHashCode(Id);
    }

    public override string ToString()
    {
        return Title;
    }

    public static bool operator ==(LiveDisplayWorkspace? left, LiveDisplayWorkspace? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(LiveDisplayWorkspace? left, LiveDisplayWorkspace? right)
    {
        return !(left == right);
    }

    static string Normalize(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Workspace id/title 不能为空。", parameterName);

        return value.Trim();
    }
}

public enum LiveDisplaySeverity
{
    Trace,
    Info,
    Success,
    Warning,
    Error,
}
