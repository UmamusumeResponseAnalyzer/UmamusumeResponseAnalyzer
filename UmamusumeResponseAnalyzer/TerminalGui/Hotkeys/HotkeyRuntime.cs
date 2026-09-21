using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
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
            lock (gate)
            {
                if (value is not null)
                {
                    if (overlaySink is not null && !ReferenceEquals(overlaySink, value))
                        throw new InvalidOperationException(i18n.Hotkey_InputSinkAlreadyBound);
                    overlaySink = value;
                    return;
                }

                try
                {
                    HidePopupLocked();
                }
                finally
                {
                    overlaySink = null;
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
        lock (gate)
        {
            hotkeys.Clear();
            notificationShortcutRegistrations.Clear();
            HidePopupLocked();
        }
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

    internal bool TryCaptureWorkspaceSink(out IUiInputSink sink)
    {
        lock (gate)
        {
            if (popupState.Popup is not null || overlaySink is null)
            {
                sink = null!;
                return false;
            }

            sink = overlaySink;
            return true;
        }
    }

    internal void ShowPopup(HotkeyPopup popup, object? owner)
    {
        if (popup.Lines.Count == 0)
            return;

        var snapshot = SnapshotPopup();
        var sink = snapshot.Sink
            ?? throw new InvalidOperationException(i18n.Hotkey_PopupRequiresInputSink);
        var shownPopup = NormalizePopupForDisplay(
            popup with { Shortcuts = null },
            Math.Max(0, popup.ScrollOffset),
            refreshExpiresAt: true,
            EstimateVisiblePopupLines(sink),
            snapshot.AutoCloseDelay);
        var shortcuts = CreateTransientShortcutEntries(popup.Shortcuts ?? [], owner);

        lock (gate)
            DisplayPopupLocked(shownPopup, shortcuts);
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

        lock (gate)
        {
            return HidePopupLocked(snapshot.Generation) && selection is not null && selectedLineIndex >= 0
                ? () => selection.ConfirmAsync(selectedLineIndex)
                : null;
        }
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
        lock (gate)
            HidePopupLocked(generation);
    }

    void SetPopupScroll(PopupSnapshot snapshot, int scrollOffset)
    {
        if (snapshot.Popup is null)
            return;

        var popup = NormalizePopupForDisplay(
            snapshot.Popup,
            scrollOffset,
            refreshExpiresAt: true,
            EstimateVisiblePopupLines(snapshot.Sink),
            snapshot.AutoCloseDelay);
        lock (gate)
            DisplayPopupLocked(popup, expectedGeneration: snapshot.Generation);
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
            EstimateVisiblePopupLines(snapshot.Sink));
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

        lock (gate)
            DisplayPopupLocked(popup, expectedGeneration: snapshot.Generation);
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

    static int EstimateVisiblePopupLines(IUiInputSink? sink)
        => Math.Max(1, sink?.PopupVisibleLineCount ?? 1);

    PopupSnapshot SnapshotPopup()
    {
        lock (gate)
            return new(popupState.Popup, popupState.Generation, overlaySink, popupAutoCloseDelay);
    }

    // The sink only queues UI work. Keep state changes and queue order under the same lock.
    void DisplayPopupLocked(
        HotkeyPopup popup,
        IReadOnlyList<TransientShortcutEntry>? shortcuts = null,
        int? expectedGeneration = null)
    {
        if (expectedGeneration is not null && expectedGeneration.Value != popupState.Generation)
            return;

        var sink = overlaySink
            ?? throw new InvalidOperationException(i18n.Hotkey_PopupRequiresInputSink);
        CancelAndDispose(popupState.AutoClose);
        var autoClose = popup.ExpiresAt is null ? null : new CancellationTokenSource();
        var generation = unchecked(popupState.Generation + 1);
        popupState = new(popup, shortcuts ?? popupState.Shortcuts, autoClose, generation);
        try
        {
            sink.ShowPopup(popup);
        }
        catch
        {
            popupState = new(null, [], null, unchecked(generation + 1));
            CancelAndDispose(autoClose);
            throw;
        }
        if (autoClose is not null)
            _ = AutoClosePopupAsync(generation, popup.ExpiresAt!.Value, autoClose.Token);
    }

    bool HidePopupLocked(int? expectedGeneration = null)
    {
        if (expectedGeneration is not null && expectedGeneration.Value != popupState.Generation ||
            popupState.Popup is null)
        {
            return false;
        }

        CancelAndDispose(popupState.AutoClose);
        popupState = new(null, [], null, unchecked(popupState.Generation + 1));
        overlaySink?.HidePopup();
        return true;
    }

    static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null)
            return;
        source.Cancel();
        source.Dispose();
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

    readonly record struct PopupState(
        HotkeyPopup? Popup,
        IReadOnlyList<TransientShortcutEntry> Shortcuts,
        CancellationTokenSource? AutoClose,
        int Generation);

    readonly record struct PopupSnapshot(
        HotkeyPopup? Popup,
        int Generation,
        IUiInputSink? Sink,
        TimeSpan AutoCloseDelay);

    sealed record TransientShortcutEntry(
        ConsoleKey Key,
        ConsoleModifiers Modifiers,
        HotkeyManager.HotkeyEntry Entry);

    sealed record NotificationShortcutRegistration(
        DateTimeOffset ExpiresAt,
        IReadOnlyList<TransientShortcutEntry> Shortcuts);

}
