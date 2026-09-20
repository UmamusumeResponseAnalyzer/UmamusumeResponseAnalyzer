using System.Collections.Frozen;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal sealed class HotkeyRuntime
{
    readonly object gate = new();
    readonly Dictionary<(ConsoleKey Key, ConsoleModifiers Modifiers), HotkeyManager.HotkeyEntry> hotkeys = [];
    readonly Dictionary<long, NotificationShortcutRegistration> notificationShortcutRegistrations = [];

    PopupState popupState = new(null, [], null, 0);
    IUiInputSink? overlaySink;
    long overlaySinkGeneration;
    int activeSinkCallouts;
    bool overlaySinkDetaching;
    TimeSpan popupAutoCloseDelay = TimeSpan.FromSeconds(3);
    long notificationShortcutRegistrationId;

    internal TimeSpan PopupAutoCloseDelay
    {
        get
        {
            lock (gate)
                return popupAutoCloseDelay;
        }
        set
        {
            lock (gate)
                popupAutoCloseDelay = value;
        }
    }

    internal IUiInputSink? OverlaySink
    {
        get
        {
            lock (gate)
                return overlaySink;
        }
        set
        {
            PopupTransitionEffect detachEffect = default;
            lock (gate)
            {
                while (overlaySinkDetaching)
                    Monitor.Wait(gate);

                if (value is not null)
                {
                    if (ReferenceEquals(overlaySink, value))
                        return;
                    if (overlaySink is not null)
                    {
                        throw new InvalidOperationException(
                            "UI input sink 已绑定；必须先完整 detach，不能原地替换 Host。");
                    }

                    overlaySink = value;
                    overlaySinkGeneration = unchecked(overlaySinkGeneration + 1);
                    return;
                }

                if (overlaySink is null)
                    return;

                overlaySinkDetaching = true;
                detachEffect = TransitionPopupLocked(PopupTransitionKind.Detach);
                overlaySink = null;
                overlaySinkGeneration = unchecked(overlaySinkGeneration + 1);
                while (activeSinkCallouts > 0)
                    Monitor.Wait(gate);
            }

            try
            {
                ApplyPopupTransition(detachEffect);
            }
            finally
            {
                lock (gate)
                {
                    overlaySinkDetaching = false;
                    Monitor.PulseAll(gate);
                }
            }
        }
    }

    internal bool HasPriorityPopup
    {
        get
        {
            lock (gate)
                return popupState.Popup is not null;
        }
    }

    internal bool HasSelectablePopup
    {
        get
        {
            lock (gate)
                return popupState.Popup?.Selection?.LineIndexes.Count > 0;
        }
    }

    internal void Register(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        HotkeyManager.HotkeyEntry entry)
    {
        lock (gate)
            hotkeys[(key, modifiers)] = entry;
    }

    internal HotkeyManager.HotkeyEntry? RegisterTracked(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        HotkeyManager.HotkeyEntry entry)
    {
        lock (gate)
        {
            var combo = (key, modifiers);
            hotkeys.Remove(combo, out var replaced);
            hotkeys.Add(combo, entry);
            return replaced;
        }
    }

    internal void RestoreTracked(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        HotkeyManager.HotkeyEntry entry,
        HotkeyManager.HotkeyEntry? replaced)
    {
        lock (gate)
        {
            var combo = (key, modifiers);
            if (!hotkeys.TryGetValue(combo, out var registered))
            {
                if (replaced is not null &&
                    !ReferenceEquals(replaced.Owner, entry.Owner))
                {
                    hotkeys.Add(combo, replaced);
                }
                return;
            }
            if (!ReferenceEquals(registered, entry))
                return;

            if (replaced is null)
                hotkeys.Remove(combo);
            else
                hotkeys[combo] = replaced;
        }
    }

    internal bool Unregister(ConsoleKey key, ConsoleModifiers modifiers)
    {
        lock (gate)
            return hotkeys.Remove((key, modifiers));
    }

    internal bool Unregister(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        HotkeyManager.HotkeyEntry entry)
    {
        lock (gate)
        {
            var combo = (key, modifiers);
            return hotkeys.TryGetValue(combo, out var registered) &&
                   ReferenceEquals(registered, entry) &&
                   hotkeys.Remove(combo);
        }
    }

    internal void Clear()
    {
        PopupTransitionEffect effect;
        lock (gate)
        {
            hotkeys.Clear();
            notificationShortcutRegistrations.Clear();
            effect = TransitionPopupLocked(PopupTransitionKind.Hide);
        }
        ApplyPopupTransition(effect);
    }

    internal int UnregisterByOwner(object owner)
    {
        lock (gate)
        {
            var count = 0;
            foreach (var combo in hotkeys
                .Where(x => ReferenceEquals(x.Value.Owner, owner))
                .Select(x => x.Key)
                .ToArray())
            {
                hotkeys.Remove(combo);
                count++;
            }

            foreach (var (id, registration) in notificationShortcutRegistrations.ToArray())
            {
                var remaining = registration.Shortcuts
                    .Where(x => !ReferenceEquals(x.Entry.Owner, owner))
                    .ToArray();
                count += registration.Shortcuts.Count - remaining.Length;
                if (remaining.Length == 0)
                    notificationShortcutRegistrations.Remove(id);
                else if (remaining.Length != registration.Shortcuts.Count)
                    notificationShortcutRegistrations[id] = registration with { Shortcuts = remaining };
            }

            var popupShortcuts = popupState.Shortcuts
                .Where(x => !ReferenceEquals(x.Entry.Owner, owner))
                .ToArray();
            count += popupState.Shortcuts.Count - popupShortcuts.Length;
            if (popupShortcuts.Length != popupState.Shortcuts.Count)
                popupState = popupState with { Shortcuts = popupShortcuts };

            return count;
        }
    }

    internal IReadOnlyDictionary<(ConsoleKey Key, ConsoleModifiers Modifiers), HotkeyManager.HotkeyEntry>
        SnapshotHotkeys()
    {
        lock (gate)
            return hotkeys.ToFrozenDictionary();
    }

    internal HotkeyManager.HotkeyEntry? FindHotkey(
        ConsoleKey key,
        ConsoleModifiers modifiers)
    {
        lock (gate)
            return hotkeys.GetValueOrDefault((key, modifiers));
    }

    internal HotkeyManager.HotkeyEntry? FindPopupShortcut(
        ConsoleKey key,
        ConsoleModifiers modifiers)
    {
        lock (gate)
        {
            return popupState.Popup is null
                ? null
                : popupState.Shortcuts.LastOrDefault(x =>
                    x.Key == key && x.Modifiers == modifiers)?.Entry;
        }
    }

    internal HotkeyManager.HotkeyEntry? FindNotificationShortcut(
        ConsoleKey key,
        ConsoleModifiers modifiers,
        DateTimeOffset now)
    {
        HotkeyManager.HotkeyEntry? entry = null;
        var latestRegistrationId = 0L;
        lock (gate)
        {
            RemoveExpiredNotificationShortcutsLocked(now);
            foreach (var (id, registration) in notificationShortcutRegistrations)
            {
                if (id <= latestRegistrationId)
                    continue;

                var candidate = registration.Shortcuts.LastOrDefault(x =>
                    x.Key == key && x.Modifiers == modifiers);
                if (candidate is null)
                    continue;

                latestRegistrationId = id;
                entry = candidate.Entry;
            }
        }
        return entry;
    }

    internal bool TryCaptureWorkspaceSink(out WorkspaceSinkSnapshot snapshot)
    {
        lock (gate)
        {
            if (popupState.Popup is not null || overlaySink is null)
            {
                snapshot = default;
                return false;
            }

            snapshot = new(overlaySink, overlaySinkGeneration);
            return true;
        }
    }

    internal async Task<bool> TryHandleWorkspaceCommandAsync(
        WorkspaceSinkSnapshot snapshot,
        Command command)
    {
        if (!TryBeginSinkCallout(snapshot.Sink, snapshot.Generation))
            return false;
        try
        {
            return await snapshot.Sink.TryHandleWorkspaceCommandAsync(command);
        }
        finally
        {
            EndSinkCallout();
        }
    }

    internal void ShowPopup(HotkeyPopup popup, object? owner)
    {
        if (popup.Lines.Count == 0)
            return;

        var snapshot = SnapshotPopup();
        var sink = snapshot.Sink
            ?? throw new InvalidOperationException("Hotkey popup 需要先绑定 UI input sink。");
        var shownPopup = NormalizePopupForDisplay(
            popup with { Shortcuts = null },
            Math.Max(0, popup.ScrollOffset),
            refreshExpiresAt: true,
            EstimateVisiblePopupLines(sink, snapshot.SinkGeneration),
            snapshot.AutoCloseDelay);
        var shortcuts = CreateTransientShortcutEntries(popup.Shortcuts ?? [], owner);

        PopupTransitionEffect effect;
        lock (gate)
        {
            if (overlaySink is null ||
                overlaySinkGeneration != snapshot.SinkGeneration ||
                !ReferenceEquals(overlaySink, sink))
            {
                throw new InvalidOperationException("Hotkey popup 的 UI input sink 已 detach。");
            }
            effect = TransitionPopupLocked(
                PopupTransitionKind.Display,
                shownPopup,
                shortcuts);
        }
        ApplyPopupTransition(effect);
    }

    internal void HidePopup()
        => HidePopup(null);

    internal void ScrollPopup(int delta)
    {
        var snapshot = SnapshotPopup();
        if (snapshot.Popup is null)
            return;
        SetPopupScroll(snapshot, snapshot.Popup.ScrollOffset + delta);
    }

    internal void SetPopupScroll(int scrollOffset)
        => SetPopupScroll(SnapshotPopup(), scrollOffset);

    internal void MovePopupSelection(int delta)
    {
        var snapshot = SnapshotPopup();
        var selection = snapshot.Popup?.Selection;
        if (selection is null)
            return;
        SetPopupSelection(snapshot, selection.BoundedSelectedIndex + delta);
    }

    internal void SetPopupSelection(int selectedIndex)
        => SetPopupSelection(SnapshotPopup(), selectedIndex);

    internal Func<Task>? CloseAndGetPopupSelectionHandler()
    {
        var snapshot = SnapshotPopup();
        var selection = snapshot.Popup?.Selection?.Normalize();
        var selectedLineIndex = selection?.SelectedLineIndex ?? -1;

        PopupTransitionEffect effect;
        lock (gate)
        {
            effect = TransitionPopupLocked(
                PopupTransitionKind.Hide,
                expectedGeneration: snapshot.Generation);
        }
        ApplyPopupTransition(effect);
        return effect.Changed && selection is not null && selectedLineIndex >= 0
            ? () => selection.ConfirmAsync(selectedLineIndex)
            : null;
    }

    internal long RegisterNotificationShortcuts(
        DateTimeOffset expiresAt,
        IReadOnlyList<UiShortcut> shortcuts,
        object? owner)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        if (shortcuts.Count == 0)
            return 0;

        var entries = CreateTransientShortcutEntries(shortcuts, owner);
        lock (gate)
        {
            RemoveExpiredNotificationShortcutsLocked(DateTimeOffset.Now);
            var id = unchecked(++notificationShortcutRegistrationId);
            notificationShortcutRegistrations[id] = new(expiresAt, entries);
            return id;
        }
    }

    internal void UnregisterNotificationShortcuts(long registrationId)
    {
        if (registrationId == 0)
            return;
        lock (gate)
            notificationShortcutRegistrations.Remove(registrationId);
    }

    void HidePopup(int? generation)
    {
        PopupTransitionEffect effect;
        lock (gate)
        {
            effect = TransitionPopupLocked(
                PopupTransitionKind.Hide,
                expectedGeneration: generation);
        }
        ApplyPopupTransition(effect);
    }

    void SetPopupScroll(PopupSnapshot snapshot, int scrollOffset)
    {
        if (snapshot.Popup is null)
            return;

        var popup = NormalizePopupForDisplay(
            snapshot.Popup,
            scrollOffset,
            refreshExpiresAt: true,
            EstimateVisiblePopupLines(snapshot.Sink, snapshot.SinkGeneration),
            snapshot.AutoCloseDelay);
        PopupTransitionEffect effect;
        lock (gate)
        {
            effect = TransitionPopupLocked(
                PopupTransitionKind.Display,
                popup,
                expectedGeneration: snapshot.Generation);
        }
        ApplyPopupTransition(effect);
    }

    void SetPopupSelection(PopupSnapshot snapshot, int selectedIndex)
    {
        if (snapshot.Popup?.Selection is null)
            return;

        var normalizedSelection = snapshot.Popup.Selection.Normalize();
        var nextSelection = normalizedSelection with
        {
            SelectedIndex = Math.Clamp(selectedIndex, 0, normalizedSelection.LineIndexes.Count - 1)
        };
        var visibleCount = Math.Min(
            snapshot.Popup.Lines.Count,
            EstimateVisiblePopupLines(snapshot.Sink, snapshot.SinkGeneration));
        var popup = snapshot.Popup with
        {
            ScrollOffset = ScrollOffsetForSelectedLine(
                snapshot.Popup.ScrollOffset,
                nextSelection.SelectedLineIndex,
                visibleCount,
                snapshot.Popup.Lines.Count),
            Selection = nextSelection,
            ExpiresAt = null
        };

        PopupTransitionEffect effect;
        lock (gate)
        {
            effect = TransitionPopupLocked(
                PopupTransitionKind.Display,
                popup,
                expectedGeneration: snapshot.Generation);
        }
        ApplyPopupTransition(effect);
    }

    static HotkeyPopup NormalizePopupForDisplay(
        HotkeyPopup popup,
        int scrollOffset,
        bool refreshExpiresAt,
        int visibleCount,
        TimeSpan autoCloseDelay)
    {
        var selection = popup.Selection?.LineIndexes.Count > 0
            ? popup.Selection.Normalize()
            : null;
        visibleCount = Math.Min(popup.Lines.Count, visibleCount);
        scrollOffset = selection is null
            ? ClampPopupScroll(popup.Lines.Count, visibleCount, scrollOffset)
            : ScrollOffsetForSelectedLine(
                scrollOffset,
                selection.SelectedLineIndex,
                visibleCount,
                popup.Lines.Count);
        return popup with
        {
            ScrollOffset = scrollOffset,
            Selection = selection,
            ExpiresAt = selection is null && refreshExpiresAt
                ? GetPopupExpiresAt(autoCloseDelay)
                : null
        };
    }

    static int ClampPopupScroll(int lineCount, int visibleCount, int scrollOffset)
        => Math.Clamp(scrollOffset, 0, Math.Max(0, lineCount - visibleCount));

    static int ScrollOffsetForSelectedLine(
        int scrollOffset,
        int selectedLineIndex,
        int visibleCount,
        int lineCount)
    {
        scrollOffset = ClampPopupScroll(lineCount, visibleCount, scrollOffset);
        if (selectedLineIndex < 0)
            return scrollOffset;
        if (selectedLineIndex < scrollOffset)
            return selectedLineIndex;
        return selectedLineIndex >= scrollOffset + visibleCount
            ? ClampPopupScroll(lineCount, visibleCount, selectedLineIndex - visibleCount + 1)
            : scrollOffset;
    }

    static DateTimeOffset? GetPopupExpiresAt(TimeSpan delay)
        => delay <= TimeSpan.Zero ? null : DateTimeOffset.Now.Add(delay);

    int EstimateVisiblePopupLines(IUiInputSink? sink, long sinkGeneration)
    {
        if (sink is null || !TryBeginSinkCallout(sink, sinkGeneration))
            return 1;
        try
        {
            return Math.Max(1, sink.PopupVisibleLineCount);
        }
        finally
        {
            EndSinkCallout();
        }
    }

    PopupSnapshot SnapshotPopup()
    {
        lock (gate)
        {
            return new(
                popupState.Popup,
                popupState.Generation,
                overlaySink,
                overlaySinkGeneration,
                popupAutoCloseDelay);
        }
    }

    PopupTransitionEffect TransitionPopupLocked(
        PopupTransitionKind kind,
        HotkeyPopup? popup = null,
        IReadOnlyList<TransientShortcutEntry>? shortcuts = null,
        int? expectedGeneration = null)
    {
        if (expectedGeneration is not null && expectedGeneration.Value != popupState.Generation)
            return default;

        if (kind is PopupTransitionKind.Hide or PopupTransitionKind.Detach)
        {
            if (kind == PopupTransitionKind.Hide &&
                popupState.Popup is null &&
                popupState.Shortcuts.Count == 0 &&
                popupState.AutoClose is null)
            {
                return default;
            }

            var wasVisible = popupState.Popup is not null;
            var previousAutoClose = popupState.AutoClose;
            var generation = unchecked(popupState.Generation + 1);
            popupState = new(null, [], null, generation);
            return new(
                true,
                kind == PopupTransitionKind.Detach
                    ? PopupRenderAction.Detach
                    : wasVisible ? PopupRenderAction.Hide : PopupRenderAction.None,
                overlaySink,
                null,
                generation,
                overlaySinkGeneration,
                previousAutoClose,
                null);
        }

        ArgumentNullException.ThrowIfNull(popup);
        var nextAutoClose = popup.ExpiresAt is null ? null : new CancellationTokenSource();
        var nextGeneration = unchecked(popupState.Generation + 1);
        var oldAutoClose = popupState.AutoClose;
        popupState = new(
            popup,
            shortcuts ?? popupState.Shortcuts,
            nextAutoClose,
            nextGeneration);
        return new(
            true,
            PopupRenderAction.Show,
            overlaySink,
            popup,
            nextGeneration,
            overlaySinkGeneration,
            oldAutoClose,
            nextAutoClose is null
                ? null
                : new(
                    nextGeneration,
                    popup.ExpiresAt!.Value,
                    nextAutoClose,
                    nextAutoClose.Token));
    }

    void ApplyPopupTransition(PopupTransitionEffect effect)
    {
        CancelAndDispose(effect.PreviousAutoClose);
        if (!effect.Changed)
            return;

        switch (effect.Render)
        {
            case PopupRenderAction.Show:
                if (effect.Sink is null ||
                    !TryBeginSinkCallout(effect.Sink, effect.SinkGeneration))
                {
                    return;
                }
                try
                {
                    try
                    {
                        effect.Sink.ShowPopup(effect.Popup!, effect.Generation);
                    }
                    finally
                    {
                        EndSinkCallout();
                    }
                }
                catch
                {
                    PopupTransitionEffect rollback;
                    lock (gate)
                    {
                        rollback = TransitionPopupLocked(
                            PopupTransitionKind.Hide,
                            expectedGeneration: effect.Generation);
                    }
                    CancelAndDispose(rollback.PreviousAutoClose);
                    throw;
                }
                break;
            case PopupRenderAction.Hide:
                if (effect.Sink is null ||
                    !TryBeginSinkCallout(effect.Sink, effect.SinkGeneration))
                {
                    return;
                }
                try
                {
                    effect.Sink.HidePopup(effect.Generation);
                }
                finally
                {
                    EndSinkCallout();
                }
                break;
            case PopupRenderAction.Detach:
                effect.Sink!.HidePopup(effect.Generation);
                break;
        }

        if (effect.AutoClose is { } autoClose)
        {
            lock (gate)
            {
                if (popupState.Generation != autoClose.Generation ||
                    !ReferenceEquals(popupState.AutoClose, autoClose.Source))
                {
                    return;
                }
            }
            _ = AutoClosePopupAsync(
                autoClose.Generation,
                autoClose.ExpiresAt,
                autoClose.CancellationToken);
        }
    }

    static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null)
            return;
        source.Cancel();
        source.Dispose();
    }

    bool TryBeginSinkCallout(IUiInputSink sink, long sinkGeneration)
    {
        lock (gate)
        {
            if (overlaySinkDetaching ||
                overlaySinkGeneration != sinkGeneration ||
                !ReferenceEquals(overlaySink, sink))
            {
                return false;
            }

            activeSinkCallouts++;
            return true;
        }
    }

    void EndSinkCallout()
    {
        lock (gate)
        {
            activeSinkCallouts--;
            if (activeSinkCallouts == 0)
                Monitor.PulseAll(gate);
        }
    }

    async Task AutoClosePopupAsync(
        int generation,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var delay = expiresAt - DateTimeOffset.Now;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
                HidePopup(generation);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
            HidePopup(generation);
        }
    }

    static TransientShortcutEntry[] CreateTransientShortcutEntries(
        IReadOnlyList<UiShortcut> shortcuts,
        object? owner)
    {
        var entries = new TransientShortcutEntry[shortcuts.Count];
        for (var i = 0; i < shortcuts.Count; i++)
        {
            var shortcut = shortcuts[i];
            ArgumentNullException.ThrowIfNull(shortcut);
            ArgumentNullException.ThrowIfNull(shortcut.Handler);
            entries[i] = new(
                shortcut.Key,
                shortcut.Modifiers,
                new(string.Empty, shortcut.Handler, owner));
        }
        return entries;
    }

    void RemoveExpiredNotificationShortcutsLocked(DateTimeOffset now)
    {
        foreach (var id in notificationShortcutRegistrations
            .Where(x => x.Value.ExpiresAt <= now)
            .Select(x => x.Key)
            .ToArray())
        {
            notificationShortcutRegistrations.Remove(id);
        }
    }

    enum PopupTransitionKind
    {
        Display,
        Hide,
        Detach
    }

    enum PopupRenderAction
    {
        None,
        Show,
        Hide,
        Detach
    }

    readonly record struct PopupState(
        HotkeyPopup? Popup,
        IReadOnlyList<TransientShortcutEntry> Shortcuts,
        CancellationTokenSource? AutoClose,
        int Generation);

    readonly record struct PopupSnapshot(
        HotkeyPopup? Popup,
        int Generation,
        IUiInputSink? Sink,
        long SinkGeneration,
        TimeSpan AutoCloseDelay);

    readonly record struct PopupTransitionEffect(
        bool Changed,
        PopupRenderAction Render,
        IUiInputSink? Sink,
        HotkeyPopup? Popup,
        int Generation,
        long SinkGeneration,
        CancellationTokenSource? PreviousAutoClose,
        AutoCloseRequest? AutoClose);

    readonly record struct AutoCloseRequest(
        int Generation,
        DateTimeOffset ExpiresAt,
        CancellationTokenSource Source,
        CancellationToken CancellationToken);

    sealed record TransientShortcutEntry(
        ConsoleKey Key,
        ConsoleModifiers Modifiers,
        HotkeyManager.HotkeyEntry Entry);

    sealed record NotificationShortcutRegistration(
        DateTimeOffset ExpiresAt,
        IReadOnlyList<TransientShortcutEntry> Shortcuts);

    internal readonly record struct WorkspaceSinkSnapshot(
        IUiInputSink Sink,
        long Generation);
}
