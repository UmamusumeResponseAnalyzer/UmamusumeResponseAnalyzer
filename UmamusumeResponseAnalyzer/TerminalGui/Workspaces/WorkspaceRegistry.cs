using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
namespace UmamusumeResponseAnalyzer.TerminalGui;

internal sealed class WorkspaceRegistry
{
    readonly Dictionary<string, Workspace> registrations = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Workspace> registrationOrder = [];

    internal WorkspaceRegistry()
    {
        Bootstrap = new(Workspace.BootstrapTitle);
        registrations.Add(Bootstrap.Title, Bootstrap);
        registrationOrder.Add(Bootstrap);
        Current = Bootstrap;
    }

    internal Workspace Bootstrap { get; }

    internal Workspace Current { get; private set; }

    internal Workspace[] SnapshotRegistrationOrder()
        => [.. registrationOrder];

    internal (Workspace Workspace, bool Created) Create(string title)
    {
        if (registrations.TryGetValue(title, out var existing))
            return (existing, false);

        var workspace = new Workspace(title);
        registrations.Add(workspace.Title, workspace);
        registrationOrder.Add(workspace);
        return (workspace, true);
    }

    internal void EnsureLive(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.IsRemoved)
            throw RemovedException(workspace);
        if (!registrations.TryGetValue(workspace.Title, out var registered) ||
            !ReferenceEquals(registered, workspace))
        {
            throw new InvalidOperationException(
                string.Format(i18n.Workspace_GenerationNotActive, workspace.Title));
        }
    }

    internal bool Remove(Workspace workspace, out Workspace replacement)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (ReferenceEquals(workspace, Bootstrap))
        {
            throw new InvalidOperationException(
                string.Format(i18n.Workspace_BootstrapCannotRemove, Bootstrap.Title));
        }
        if (workspace.IsRemoved)
        {
            replacement = Current;
            return false;
        }

        EnsureLive(workspace);
        registrations.Remove(workspace.Title);
        registrationOrder.Remove(workspace);
        workspace.IsRemoved = true;
        if (ReferenceEquals(Current, workspace))
            Current = registrationOrder[0];
        replacement = Current;
        return true;
    }

    internal void SwitchTo(Workspace workspace)
    {
        EnsureLive(workspace);
        Current = workspace;
    }

    internal static InvalidOperationException RemovedException(Workspace workspace)
        => new(string.Format(i18n.Workspace_Removed, workspace.Title));
}
