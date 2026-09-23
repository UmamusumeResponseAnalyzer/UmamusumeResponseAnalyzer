using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using System.Text;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Commands;

internal static class HostCommands
{
    internal sealed record WorkspaceItem(
        Workspace Handle,
        string? ShortcutText = null);

    internal sealed record Snapshot(
        IReadOnlyList<WorkspaceItem> Workspaces,
        Workspace CurrentWorkspace,
        IReadOnlyList<PluginManager.PluginRuntimeStatus> Plugins);

    internal sealed record DisplayItem(
        string Text,
        Workspace? Workspace = null);

    internal sealed record Display(
        string Title,
        IReadOnlyList<DisplayItem> Items,
        int? SelectedIndex = null);

    internal sealed record Result(
        string? Message = null,
        UiSeverity Severity = UiSeverity.Info,
        Display? Display = null,
        Workspace? SwitchWorkspace = null);

    internal static Result? Execute(
        string command,
        Snapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        if (!command.StartsWith('/'))
            return null;

        var (name, arguments) = Tokenize(command[1..]);
        if (name.Length == 0)
            return Warning(i18n.Command_Empty);

        if (name.Equals("workspace", StringComparison.OrdinalIgnoreCase))
            return RunWorkspaceCommand(arguments, snapshot);
        if (name.Equals("plugin", StringComparison.OrdinalIgnoreCase))
            return RunPluginCommand(arguments, snapshot);

        return Warning(string.Format(i18n.Command_Unknown, name));
    }

    internal static IReadOnlyList<string> Complete(string input, Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!input.StartsWith('/'))
            return [];

        var body = input[1..];
        var spaceIndex = body.IndexOf(' ');
        if (spaceIndex < 0)
            return CompleteByPrefix(input, ["/plugin", "/workspace"]);

        var name = body[..spaceIndex];
        var rest = body[(spaceIndex + 1)..];
        return name.ToLowerInvariant() switch
        {
            "workspace" => CompleteWorkspaceCommand(rest, snapshot.Workspaces),
            "plugin" => CompleteByPrefix($"/plugin {rest}", ["/plugin list"]),
            _ => []
        };
    }

    internal static (string Token, string Remainder) Tokenize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value = value.Trim();
        if (value.Length == 0)
            return (string.Empty, string.Empty);

        var index = value.IndexOf(' ');
        return index < 0
            ? (value, string.Empty)
            : (value[..index], value[(index + 1)..].Trim());
    }

    internal static string ParseWorkspaceTitle(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.StartsWith('"'))
            return value;

        var title = new StringBuilder(value.Length);
        for (var i = 1; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '"':
                    if (i != value.Length - 1)
                    {
                        throw new FormatException(
                            i18n.Command_QuotedTitleTrailingContent);
                    }
                    if (string.IsNullOrWhiteSpace(title.ToString()))
                    {
                        throw new FormatException(
                            i18n.Command_QuotedTitleEmpty);
                    }
                    return title.ToString();
                case '\\':
                    if (++i >= value.Length)
                        throw new FormatException(i18n.Command_QuotedTitleTrailingSlash);
                    if (value[i] is not ('"' or '\\'))
                    {
                        throw new FormatException(
                            string.Format(i18n.Command_QuotedTitleUnsupportedEscape, value[i]));
                    }
                    title.Append(value[i]);
                    break;
                default:
                    title.Append(value[i]);
                    break;
            }
        }

        throw new FormatException(i18n.Command_QuotedTitleUnclosed);
    }

    static Result RunWorkspaceCommand(string arguments, Snapshot snapshot)
    {
        var (subcommand, remainder) = Tokenize(arguments);
        if (subcommand.Length == 0)
            return ShowWorkspaces(snapshot, selectable: true);

        if (subcommand.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return remainder.Length == 0
                ? ShowWorkspaces(snapshot, selectable: false)
                : Warning(i18n.Command_WorkspaceUsage);
        }

        if (subcommand.Equals("switch", StringComparison.OrdinalIgnoreCase))
        {
            if (remainder.Length == 0)
                return ShowWorkspaces(snapshot, selectable: true);

            try
            {
                var title = ParseWorkspaceTitle(remainder);
                var workspace = snapshot.Workspaces
                    .Select(item => item.Handle)
                    .FirstOrDefault(candidate =>
                        candidate.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
                return workspace is null
                    ? Warning(string.Format(i18n.Command_WorkspaceNotFound, title))
                    : new(SwitchWorkspace: workspace);
            }
            catch (FormatException ex)
            {
                return new(ex.Message, UiSeverity.Error);
            }
        }

        return Warning(i18n.Command_WorkspaceUsage);
    }

    static Result RunPluginCommand(string arguments, Snapshot snapshot)
        => arguments.Length == 0 || arguments.Equals("list", StringComparison.OrdinalIgnoreCase)
            ? ShowPlugins(snapshot.Plugins)
            : Warning(i18n.Command_PluginUsage);

    static Result ShowWorkspaces(Snapshot snapshot, bool selectable)
    {
        var items = snapshot.Workspaces.Select(item =>
        {
            var marker = ReferenceEquals(item.Handle, snapshot.CurrentWorkspace) ? "*" : " ";
            var shortcut = string.IsNullOrEmpty(item.ShortcutText)
                ? string.Empty
                : $" [{item.ShortcutText}]";
            var title = item.Handle.DisplayTitle == item.Handle.Title
                ? item.Handle.DisplayTitle
                : $"{item.Handle.DisplayTitle} ({item.Handle.Title})";
            return new DisplayItem(
                $"{marker} {title}{shortcut}",
                item.Handle);
        }).ToArray();
        int? selectedIndex = selectable
            ? Math.Max(0, Array.FindIndex(
                items,
                item => ReferenceEquals(item.Workspace, snapshot.CurrentWorkspace)))
            : null;
        return new(Display: new(i18n.Command_WorkspacesTitle, items, selectedIndex));
    }

    static Result ShowPlugins(IReadOnlyList<PluginManager.PluginRuntimeStatus> plugins)
    {
        if (plugins.Count == 0)
            return new(Display: new(i18n.Command_PluginsTitle, [new(i18n.Command_NoPlugins)]));

        return new(Display: new(
            i18n.Command_PluginsTitle,
            plugins.Select(plugin =>
            {
                var state = plugin.IsLoaded
                    ? i18n.Command_PluginStateLoaded
                    : plugin.Error is not null || !plugin.IsAvailable
                        ? i18n.Command_PluginStateFailed
                        : i18n.Command_PluginStateUnloaded;
                var displayName = plugin.DisplayName == plugin.InternalName
                    ? string.Empty
                    : $" ({plugin.DisplayName})";
                var version = plugin.Version is null ? string.Empty : $" v{plugin.Version}";
                var author = string.IsNullOrWhiteSpace(plugin.Author)
                    ? string.Empty
                    : " " + string.Format(i18n.Command_PluginAuthor, plugin.Author);
                var error = plugin.Error is null ? string.Empty : $" — {plugin.Error}";
                return new DisplayItem(
                    $"{state} {plugin.InternalName}{displayName}{version}{author}{error}");
            }).ToArray()));
    }

    static IReadOnlyList<string> CompleteWorkspaceCommand(
        string rest,
        IReadOnlyList<WorkspaceItem> workspaces)
    {
        var subcommandSpaceIndex = rest.IndexOf(' ');
        if (subcommandSpaceIndex < 0)
        {
            return CompleteByPrefix($"/workspace {rest}",
            [
                "/workspace list",
                "/workspace switch"
            ]);
        }

        var subcommand = rest[..subcommandSpaceIndex];
        var arguments = rest[(subcommandSpaceIndex + 1)..];
        return subcommand.Equals("switch", StringComparison.OrdinalIgnoreCase)
            ? CompleteWorkspaceTitles(arguments, workspaces)
            : [];
    }

    static IReadOnlyList<string> CompleteWorkspaceTitles(
        string arguments,
        IReadOnlyList<WorkspaceItem> workspaces)
    {
        var prefix = WorkspaceCompletionPrefix(arguments);
        return workspaces
            .Select(item => item.Handle.Title)
            .Where(title => title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(title => $"/workspace switch {QuoteWorkspaceTitleIfNeeded(title)}")
            .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static string WorkspaceCompletionPrefix(string value)
    {
        if (!value.StartsWith('"'))
            return value;

        var prefix = new StringBuilder(value.Length);
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] == '"')
                break;
            if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] is '"' or '\\')
                i++;
            prefix.Append(value[i]);
        }
        return prefix.ToString();
    }

    static string QuoteWorkspaceTitleIfNeeded(string title)
    {
        if (!title.Any(char.IsWhiteSpace) && !title.Contains('"') && !title.Contains('\\'))
            return title;

        return $"\"{title.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    static IReadOnlyList<string> CompleteByPrefix(
        string prefix,
        IEnumerable<string> candidates)
        => candidates
            .Where(candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    static Result Warning(string message)
        => new(message, UiSeverity.Warning);

}
