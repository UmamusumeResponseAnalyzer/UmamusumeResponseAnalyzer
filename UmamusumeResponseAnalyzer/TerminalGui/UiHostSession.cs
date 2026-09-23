using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using Terminal.Gui.Input;
using UmamusumeResponseAnalyzer.Commands;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal sealed class UiHostSession
{
    const int MaxIngressBatch = 256;

    readonly object gate = new();
    readonly WorkspaceRegistry registry = new();
    readonly HashSet<(Workspace Workspace, string Key)> admittedPanels = [];
    readonly HashSet<HotkeyManager.HotkeyEntry> pendingWorkspaceHotkeys =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<Workspace, WorkspaceHotkey> workspaceHotkeys =
        new(ReferenceEqualityComparer.Instance);
    readonly Queue<UiHostIngress> events = [];

    Lifecycle lifecycle;
    long panelSequence;
    bool drainRunning;
    bool drainScheduled;
    bool runClaimed;
    bool rootCreated;
    Exception? rootCreationFailure;

    internal bool IsRunning
    {
        get
        {
            lock (gate)
                return lifecycle == Lifecycle.Running;
        }
    }

    internal Workspace Bootstrap => registry.Bootstrap;

    internal void EnsureAvailable()
    {
        lock (gate)
            EnsureAvailableLocked();
    }

    internal Workspace GetCurrentWorkspace()
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            return registry.Current;
        }
    }

    internal Workspace CreateWorkspace(string title, out bool schedule)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            var (workspace, created) = registry.Create(title);
            if (!created)
            {
                schedule = false;
                return workspace;
            }

            var registrationOrder = registry.SnapshotRegistrationOrder();
            schedule = AdmitLocked(new RegisterWorkspaceIngress(
                workspace,
                registrationOrder,
                registry.Current));
            return workspace;
        }
    }

    internal bool RemoveWorkspace(Workspace workspace, out bool schedule)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            if (!registry.Remove(workspace, out var replacement))
            {
                schedule = false;
                return false;
            }

            admittedPanels.RemoveWhere(key => ReferenceEquals(key.Workspace, workspace));
            var registrationOrder = registry.SnapshotRegistrationOrder();
            schedule = AdmitLocked(new RemoveWorkspaceIngress(
                workspace,
                replacement,
                registrationOrder));
            return true;
        }
    }

    internal bool SetPanel(
        Workspace workspace,
        string key,
        string title,
        WorkspaceContent content,
        bool fullBleed,
        bool switchToWorkspace)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            registry.EnsureLive(workspace);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            ArgumentNullException.ThrowIfNull(content);
            if (switchToWorkspace)
                registry.SwitchTo(workspace);
            admittedPanels.Add((workspace, key));
            return AdmitLocked(new SetPanelIngress(
                new(
                    workspace,
                    key,
                    title,
                    content,
                    unchecked(++panelSequence),
                    fullBleed),
                switchToWorkspace));
        }
    }

    internal bool RemovePanel(Workspace workspace, string key, out bool schedule)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            registry.EnsureLive(workspace);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (!admittedPanels.Remove((workspace, key)))
            {
                schedule = false;
                return false;
            }

            schedule = AdmitLocked(new RemovePanelIngress(workspace, key));
            return true;
        }
    }

    internal bool Log(string text, UiSeverity severity, string? exceptionDetails)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            ArgumentNullException.ThrowIfNull(text);
            return AdmitLocked(new LogIngress(new(text, severity, exceptionDetails)));
        }
    }

    internal void EnsureWorkspaceAvailable(Workspace? workspace)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            if (workspace is not null)
                registry.EnsureLive(workspace);
        }
    }

    internal bool Notify(
        Workspace? workspace,
        string text,
        UiSeverity severity,
        DateTimeOffset expiresAt,
        long shortcutRegistrationId)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            if (workspace is not null)
                registry.EnsureLive(workspace);
            return AdmitLocked(new NotifyIngress(
                workspace,
                text,
                severity,
                expiresAt,
                shortcutRegistrationId));
        }
    }

    internal bool SwitchWorkspace(Workspace workspace)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            registry.SwitchTo(workspace);
            return AdmitLocked(new SwitchWorkspaceIngress(workspace));
        }
    }

    internal bool BindWorkspaceHotkey(
        Workspace workspace,
        ConsoleKey key,
        ConsoleModifiers modifiers,
        HotkeyManager.HotkeyEntry entry)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            registry.EnsureLive(workspace);
            if (modifiers.HasFlag(ConsoleModifiers.Control) &&
                key is ConsoleKey.S or ConsoleKey.Q or ConsoleKey.Z)
            {
                throw new InvalidOperationException(string.Format(i18n.Hotkey_Reserved, key));
            }

            pendingWorkspaceHotkeys.Add(entry);
            try
            {
                return AdmitLocked(new BindWorkspaceHotkeyIngress(
                    workspace,
                    key,
                    modifiers,
                    entry));
            }
            catch
            {
                pendingWorkspaceHotkeys.Remove(entry);
                throw;
            }
        }
    }

    internal int RemoveWorkspaceHotkeysByOwner(object owner)
    {
        lock (gate)
        {
            var canceled = pendingWorkspaceHotkeys.RemoveWhere(
                entry => ReferenceEquals(entry.Owner, owner));
            foreach (var workspace in workspaceHotkeys
                .Where(pair => ReferenceEquals(pair.Value.Entry.Owner, owner))
                .Select(pair => pair.Key)
                .ToArray())
            {
                workspaceHotkeys.Remove(workspace);
            }
            return canceled;
        }
    }

    internal Task Flush(out bool schedule)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            EnsureAvailableLocked();
            schedule = AdmitLocked(new FlushIngress(completion));
        }
        return completion.Task;
    }

    internal HostCommands.Snapshot GetCommandSnapshot()
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            return CreateCommandSnapshotLocked();
        }
    }

    internal void ClaimRun()
    {
        lock (gate)
        {
            if (runClaimed || lifecycle == Lifecycle.Stopped)
                throw new InvalidOperationException(i18n.Host_RunOnce);

            runClaimed = true;
            if (lifecycle == Lifecycle.Created)
                lifecycle = Lifecycle.Running;
        }
    }

    internal bool RootCreated()
    {
        lock (gate)
        {
            rootCreated = true;
            return ArmDrainLocked();
        }
    }

    internal void BeginShutdown()
    {
        lock (gate)
            lifecycle = Lifecycle.Stopping;
    }

    internal bool BeginDrain()
    {
        lock (gate)
        {
            drainScheduled = false;
            if (drainRunning)
                return false;
            drainRunning = true;
            return true;
        }
    }

    internal bool CompleteDrain()
    {
        lock (gate)
        {
            drainRunning = false;
            return ArmDrainLocked();
        }
    }

    internal List<UiHostIngress> TakeBatch()
    {
        lock (gate)
        {
            var admittedEvents = new List<UiHostIngress>(
                Math.Min(MaxIngressBatch, events.Count));
            while (admittedEvents.Count < MaxIngressBatch &&
                   events.TryDequeue(out var admitted))
            {
                admittedEvents.Add(admitted);
                if (admitted is FlushIngress)
                    break;
            }
            return admittedEvents;
        }
    }

    internal bool Navigate(Command command, TaskCompletionSource<bool> completion)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            return AdmitLocked(new NavigateWorkspaceIngress(command, completion));
        }
    }

    internal bool ShowPopup(HotkeyPopup popup)
    {
        lock (gate)
        {
            EnsureAvailableLocked();
            return AdmitLocked(new ShowPopupIngress(popup));
        }
    }

    internal bool HidePopup()
    {
        lock (gate)
        {
            if (lifecycle >= Lifecycle.Stopping || rootCreationFailure is not null)
                return false;
            return AdmitLocked(new HidePopupIngress());
        }
    }

    internal WorkspaceHotkey? RemoveWorkspaceHotkey(Workspace workspace)
    {
        lock (gate)
        {
            workspaceHotkeys.Remove(workspace, out var removed);
            return removed;
        }
    }

    internal void CancelPendingWorkspaceHotkey(HotkeyManager.HotkeyEntry entry)
    {
        lock (gate)
            pendingWorkspaceHotkeys.Remove(entry);
    }

    internal bool IsWorkspaceHotkeyPending(HotkeyManager.HotkeyEntry entry)
    {
        lock (gate)
            return pendingWorkspaceHotkeys.Contains(entry);
    }

    internal WorkspaceHotkeyCommit CommitWorkspaceHotkey(
        BindWorkspaceHotkeyIngress binding,
        HotkeyManager.HotkeyEntry? replaced,
        string shortcut)
    {
        lock (gate)
        {
            if (!pendingWorkspaceHotkeys.Remove(binding.Entry))
                return new(Canceled: true, Previous: null);

            var previous = workspaceHotkeys.GetValueOrDefault(binding.Workspace);
            KeyValuePair<Workspace, WorkspaceHotkey>[] displacedMetadata = replaced is null
                ? []
                : workspaceHotkeys
                    .Where(pair => ReferenceEquals(pair.Value.Entry, replaced))
                    .ToArray();
            workspaceHotkeys.Remove(binding.Workspace);
            foreach (var displaced in displacedMetadata)
                workspaceHotkeys.Remove(displaced.Key);
            workspaceHotkeys.Add(binding.Workspace, new(
                binding.Key,
                binding.Modifiers,
                shortcut,
                binding.Entry));
            return new(Canceled: false, previous);
        }
    }

    internal List<UiHostIngress> FailRootCreation(Exception failure)
    {
        lock (gate)
        {
            rootCreationFailure = failure;
            var abandoned = events.ToList();
            events.Clear();
            return abandoned;
        }
    }

    internal UiHostSessionFinish Finish()
    {
        WorkspaceHotkey[] hotkeys;
        lock (gate)
        {
            lifecycle = Lifecycle.Stopped;
            drainRunning = false;
            drainScheduled = false;
            hotkeys = [.. workspaceHotkeys.Values];
            workspaceHotkeys.Clear();
            pendingWorkspaceHotkeys.Clear();
            var abandoned = events.ToList();
            events.Clear();
            return new(hotkeys, abandoned);
        }
    }

    void EnsureAvailableLocked()
    {
        if (lifecycle >= Lifecycle.Stopping)
        {
            throw new InvalidOperationException(
                lifecycle == Lifecycle.Stopped ? i18n.Host_Stopped : i18n.Host_Stopping);
        }
        if (rootCreationFailure is not null)
        {
            throw new InvalidOperationException(
                i18n.Host_RootCreationFailed,
                rootCreationFailure);
        }
    }

    HostCommands.Snapshot CreateCommandSnapshotLocked()
        => new(
            registry.SnapshotRegistrationOrder().Select(workspace => new HostCommands.WorkspaceItem(
                workspace,
                workspaceHotkeys.GetValueOrDefault(workspace)?.ShortcutText)).ToArray(),
            registry.Current);

    bool AdmitLocked(UiHostIngress uiEvent)
    {
        events.Enqueue(uiEvent);
        return ArmDrainLocked();
    }

    bool ArmDrainLocked()
    {
        if (lifecycle is not (Lifecycle.Running or Lifecycle.Stopping) ||
            !rootCreated ||
            drainRunning ||
            drainScheduled ||
            events.Count == 0)
        {
            return false;
        }

        drainScheduled = true;
        return true;
    }

    enum Lifecycle
    {
        Created,
        Running,
        Stopping,
        Stopped
    }
}

internal abstract record UiHostIngress;

internal sealed record RegisterWorkspaceIngress(
    Workspace Workspace,
    Workspace[] RegistrationOrder,
    Workspace CurrentWorkspace) : UiHostIngress;

internal sealed record RemoveWorkspaceIngress(
    Workspace Workspace,
    Workspace Replacement,
    Workspace[] RegistrationOrder) : UiHostIngress;

internal sealed record SetPanelIngress(
    WorkspacePanel Panel,
    bool SwitchToWorkspace) : UiHostIngress;

internal sealed record RemovePanelIngress(
    Workspace Workspace,
    string Key) : UiHostIngress;

internal sealed record LogIngress(UiLogLine Line) : UiHostIngress;

internal sealed record NotifyIngress(
    Workspace? Workspace,
    string Text,
    UiSeverity Severity,
    DateTimeOffset ExpiresAt,
    long ShortcutRegistrationId) : UiHostIngress;

internal sealed record SwitchWorkspaceIngress(Workspace Workspace) : UiHostIngress;

internal sealed record BindWorkspaceHotkeyIngress(
    Workspace Workspace,
    ConsoleKey Key,
    ConsoleModifiers Modifiers,
    HotkeyManager.HotkeyEntry Entry) : UiHostIngress;

internal sealed record NavigateWorkspaceIngress(
    Command Command,
    TaskCompletionSource<bool> Completion) : UiHostIngress;

internal sealed record ShowPopupIngress(HotkeyPopup Popup) : UiHostIngress;

internal sealed record HidePopupIngress : UiHostIngress;

internal sealed record FlushIngress(TaskCompletionSource Completion) : UiHostIngress;

internal sealed record WorkspaceHotkey(
    ConsoleKey Key,
    ConsoleModifiers Modifiers,
    string ShortcutText,
    HotkeyManager.HotkeyEntry Entry);

internal sealed record WorkspaceHotkeyCommit(
    bool Canceled,
    WorkspaceHotkey? Previous);

internal sealed record UiHostSessionFinish(
    WorkspaceHotkey[] Hotkeys,
    List<UiHostIngress> Abandoned);
