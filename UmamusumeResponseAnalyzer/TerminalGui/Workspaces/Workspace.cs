using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
namespace UmamusumeResponseAnalyzer.TerminalGui;

public sealed class Workspace
{
    internal const string BootstrapTitle = "启动";

    internal Workspace(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException(i18n.Workspace_TitleEmpty, nameof(title));

        Title = title;
    }

    public string Title { get; }

    internal string DisplayTitle => Title == BootstrapTitle ? i18n.Workspace_BootstrapTitle : Title;

    internal bool IsRemoved { get; set; }

    public static Workspace Current => TerminalUi.RequireHost().GetCurrentWorkspace();

    public static Workspace Create(string title)
        => TerminalUi.RequireHost().CreateWorkspace(title);

    public void SetPanel(
        string key,
        string title,
        WorkspaceContent content,
        bool fullBleed = false,
        bool switchToWorkspace = true)
        => TerminalUi.RequireHost().SetPanel(
            this,
            key,
            title,
            content,
            fullBleed,
            switchToWorkspace);

    public bool RemovePanel(string key)
        => TerminalUi.RequireHost().RemovePanel(this, key);

    public void Notify(
        string text,
        UiSeverity severity = UiSeverity.Info,
        TimeSpan? ttl = null,
        params UiShortcut[] shortcuts)
        => TerminalUi.RequireHost().Notify(this, text, severity, ttl, shortcuts);

    public void SwitchTo()
        => TerminalUi.RequireHost().SwitchWorkspace(this);

    public void BindHotkey(
        ConsoleKey key,
        ConsoleModifiers modifiers = 0,
        string? description = null)
        => TerminalUi.RequireHost().BindWorkspaceHotkey(this, key, modifiers, description);

    public void Remove()
        => TerminalUi.RequireHost().RemoveWorkspace(this);
}
