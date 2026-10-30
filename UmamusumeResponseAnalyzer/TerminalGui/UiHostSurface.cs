using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using System.Collections.Immutable;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Commands;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal sealed class UiHostSurface : IDisposable
{
    // The popup is disabled so it never takes input; keep its text at full strength anyway.
    static readonly Scheme HotkeyPopupScheme = new(UraTheme.Raised)
    {
        Disabled = new Terminal.Gui.Drawing.Attribute(UraTheme.Ink, UraTheme.Surface)
    };

    readonly UiHost host;
    readonly IApplication application;
    readonly Workspace bootstrap;
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly List<NotifyIngress> notifications = [];

    Workspace[] renderedWorkspaces;
    Workspace renderedWorkspace;
    HotkeyPopup? hotkeyPopup;
    int popupVisibleLineCount = 1;
    Window? window;
    WorkspaceViewport? workspaceViewport;
    WorkspaceTaskbarView? workspaceTaskbar;
    NotificationOverlayView? notificationOverlay;
    Label? hotkeyLayer;
    CommandModeView? commandMode;
    object? overlayTimer;

    internal UiHostSurface(
        UiHost host,
        IApplication application,
        Workspace bootstrap)
    {
        this.host = host;
        this.application = application;
        this.bootstrap = bootstrap;
        renderedWorkspaces = [bootstrap];
        renderedWorkspace = bootstrap;
    }

    internal Task Ready => ready.Task;
    internal int PopupVisibleLineCount => Volatile.Read(ref popupVisibleLineCount);
    internal event Action<UiLogLine>? LogAdded;

    internal void Create()
    {
        var savedTaskbarTitleOrder = Config.WorkspaceTaskbarTitleOrder?.ToArray()
            ?? throw new InvalidOperationException(
                i18n.Host_TaskbarOrderNull);
        UraTheme.Apply();
        window = new UiHostWindow(host.RequestShutdown)
        {
            Title = "UmamusumeResponseAnalyzer",
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            BorderStyle = null
        };
        workspaceViewport = new(renderedWorkspace);
        commandMode = new(
            command => TrackInputTask(host.HandleCommandAsync(command)),
            host.CompleteCommand);
        workspaceTaskbar = new(
            renderedWorkspace,
            () => commandMode.IsOpen,
            host.SwitchWorkspace,
            savedTaskbarTitleOrder,
            titleOrder =>
            {
                Config.WorkspaceTaskbarTitleOrder = [.. titleOrder];
                Config.Save();
            });
        notificationOverlay = new();
        hotkeyLayer = new()
        {
            CanFocus = false,
            Enabled = false,
            Visible = false,
            ViewportSettings = ViewportSettingsFlags.TransparentMouse
        };
        hotkeyLayer.SetScheme(HotkeyPopupScheme);
        window.Add(
            workspaceViewport,
            workspaceTaskbar.BottomEdgeTrigger,
            workspaceTaskbar,
            notificationOverlay,
            hotkeyLayer,
            commandMode);
        window.Initialized += WindowInitialized;
        window.ViewportChanged += WindowViewportChanged;
        window.KeyDownNotHandled += WindowKeyDownNotHandled;
        window.MouseEvent += WindowMouseEvent;
        application.Mouse.MouseEvent += ApplicationMouseEvent;
        commandMode.VisibleChanged += CommandModeVisibleChanged;
        application.Keyboard.KeyDown += ApplicationKeyDown;
        application.LayoutAndDrawComplete += ApplicationLayoutAndDrawComplete;
    }

    internal Task RunAsync()
        => application.RunAsync(
            window ?? throw new InvalidOperationException(i18n.Host_WindowNotCreated),
            CancellationToken.None);

    internal void StartOverlayTimer(Func<bool> callback)
        => overlayTimer = application.AddTimeout(TimeSpan.FromMilliseconds(250), callback);

    internal void StopOverlayTimer()
    {
        if (overlayTimer is null)
            return;
        application.RemoveTimeout(overlayTimer);
        overlayTimer = null;
    }

    internal void RegisterWorkspace(RegisterWorkspaceIngress registration)
    {
        renderedWorkspaces = registration.RegistrationOrder;
        renderedWorkspace = registration.CurrentWorkspace;
        Viewport.SetActiveWorkspace(renderedWorkspace);
    }

    internal void RemoveWorkspace(RemoveWorkspaceIngress removal)
    {
        Viewport.RemoveWorkspace(removal.Workspace, removal.Replacement);
        renderedWorkspaces = removal.RegistrationOrder;
        renderedWorkspace = removal.Replacement;
        RemoveWorkspaceNotifications(removal.Workspace);
    }

    internal void SetPanel(SetPanelIngress panel)
    {
        Viewport.SetPanel(panel.Panel);
        if (!panel.SwitchToWorkspace)
            return;
        renderedWorkspace = panel.Panel.Workspace;
        Viewport.SetActiveWorkspace(renderedWorkspace);
    }

    internal void RemovePanel(RemovePanelIngress panel)
        => Viewport.RemovePanel(panel.Workspace, panel.Key);

    internal void SwitchWorkspace(Workspace workspace)
    {
        renderedWorkspace = workspace;
        Viewport.SetActiveWorkspace(renderedWorkspace);
    }

    internal bool Navigate(Command command)
        => Viewport.Navigate(command);

    internal void AddLog(UiLogLine line)
    {
        LogAdded?.Invoke(line);
    }

    internal void AddNotification(NotifyIngress notification)
    {
        if (notification.ExpiresAt <= DateTimeOffset.Now)
        {
            HotkeyManager.UnregisterNotificationShortcuts(
                notification.ShortcutRegistrationId);
            return;
        }

        notifications.Add(notification);
    }

    internal void ShowPopup(HotkeyPopup popup) => hotkeyPopup = popup;

    internal void HidePopup() => hotkeyPopup = null;

    internal void RequestApplicationStop()
    {
        if (window is not null)
            application.RequestStop(window);
    }

    internal void Reconcile(UiChange changes)
    {
        if (changes.HasFlag(UiChange.Workspace))
        {
            workspaceViewport?.Reconcile();
            workspaceTaskbar?.Refresh(renderedWorkspaces, renderedWorkspace);
            if (window is not null)
            {
                window.Title = $"UmamusumeResponseAnalyzer - {renderedWorkspace.DisplayTitle}";
                Volatile.Write(
                    ref popupVisibleLineCount,
                    Math.Max(1, window.Viewport.Height - 2));
            }
        }
        if (changes.HasFlag(UiChange.Notifications))
            RefreshNotificationOverlay();
        if (changes.HasFlag(UiChange.Hotkey))
            RefreshHotkeyOverlay();
        if (changes.HasFlag(UiChange.Bootstrap))
            host.Bootstrap.RefreshView();
    }

    internal void ExpireNotifications()
    {
        var now = DateTimeOffset.Now;
        foreach (var notification in notifications
            .Where(notification => notification.ExpiresAt <= now)
            .ToArray())
        {
            HotkeyManager.UnregisterNotificationShortcuts(
                notification.ShortcutRegistrationId);
        }
        notifications.RemoveAll(notification => notification.ExpiresAt <= now);
        RefreshNotificationOverlay();
    }

    internal void ShowCommandDisplay(HostCommands.Display display)
    {
        var lines = new List<HotkeyPopupLine> { new(display.Title) };
        lines.AddRange(display.Items.Select(item => new HotkeyPopupLine(item.Text)));
        HotkeyPopupSelection? selection = null;
        if (display.SelectedIndex is { } selectedIndex &&
            selectedIndex >= 0 &&
            selectedIndex < display.Items.Count)
        {
            selection = new(
                Enumerable.Range(1, display.Items.Count).ToArray(),
                selectedIndex,
                lineIndex =>
                {
                    display.Items[lineIndex - 1].Workspace.SwitchTo();
                    return Task.CompletedTask;
                });
        }
        HotkeyManager.ShowPopup(new HotkeyPopup(lines, Selection: selection));
    }

    internal void Finish()
    {
        foreach (var notification in notifications)
            HotkeyManager.UnregisterNotificationShortcuts(notification.ShortcutRegistrationId);
        notifications.Clear();
        ready.TrySetCanceled();
    }

    public void Dispose()
    {
        if (window is null)
            return;

        application.LayoutAndDrawComplete -= ApplicationLayoutAndDrawComplete;
        application.Keyboard.KeyDown -= ApplicationKeyDown;
        application.Mouse.MouseEvent -= ApplicationMouseEvent;
        if (commandMode is not null)
            commandMode.VisibleChanged -= CommandModeVisibleChanged;
        window.MouseEvent -= WindowMouseEvent;
        window.KeyDownNotHandled -= WindowKeyDownNotHandled;
        window.ViewportChanged -= WindowViewportChanged;
        window.Initialized -= WindowInitialized;
        window.Dispose();
        window = null;
        workspaceViewport = null;
        workspaceTaskbar = null;
        notificationOverlay = null;
        hotkeyLayer = null;
        commandMode = null;
    }

    WorkspaceViewport Viewport
        => workspaceViewport
            ?? throw new InvalidOperationException(i18n.Host_ViewportNotCreated);

    void WindowInitialized(object? sender, EventArgs e)
        => Reconcile(UiChange.All);

    void ApplicationLayoutAndDrawComplete(object? sender, EventArgs e)
    {
        if (window is null || !ReferenceEquals(application.TopRunnableView, window))
            return;

        application.LayoutAndDrawComplete -= ApplicationLayoutAndDrawComplete;
        ready.TrySetResult();
    }

    void WindowViewportChanged(object? sender, DrawEventArgs e)
        => Reconcile(UiChange.All);

    void CommandModeVisibleChanged(object? sender, EventArgs e)
        => workspaceTaskbar?.CommandModeVisibilityChanged();

    void ApplicationKeyDown(object? sender, Key key)
    {
        if (key.Handled || key.KeyCode == Key.C.WithCtrl.KeyCode)
            return;
        if (window is null ||
            !ReferenceEquals(application.TopRunnableView, window) ||
            !HotkeyManager.HasPriorityPopup)
        {
            return;
        }

        key.Handled = true;
        TrackInputTask(DispatchKeyAsync(key));
    }

    void WindowKeyDownNotHandled(object? sender, Key key)
    {
        if (key.KeyCode == Key.C.WithCtrl.KeyCode)
            return;
        if (key.KeyCode == Key.Tab.KeyCode ||
            key.KeyCode == Key.Tab.WithShift.KeyCode ||
            key.KeyCode == Key.F6.KeyCode ||
            key.KeyCode == Key.F6.WithShift.KeyCode)
        {
            return;
        }
        if (commandMode is { IsOpen: true })
        {
            key.Handled = true;
            return;
        }

        var opensCommandMode = commandMode is not null &&
            ((!key.IsCtrl && !key.IsAlt &&
              key.TryGetPrintableRune(out var rune) && rune.Value == '/') ||
             key.KeyCode == Key.Enter.KeyCode);
        var inputKey = new Key(key);
        key.Handled = true;
        TrackInputTask(opensCommandMode
            ? DispatchKeyOrOpenCommandAsync(
                inputKey,
                inputKey.KeyCode == Key.Enter.KeyCode ? string.Empty : "/")
            : DispatchKeyAsync(inputKey));
    }

    void ApplicationMouseEvent(object? sender, Mouse mouse)
    {
        if (ReferenceEquals(application.TopRunnableView, window))
            workspaceTaskbar?.HandleMousePosition(mouse);
    }

    void WindowMouseEvent(object? sender, Mouse mouse)
    {
        var flags = mouse.Flags;
        var verticalDelta = flags.HasFlag(MouseFlags.WheeledUp)
            ? 1
            : flags.HasFlag(MouseFlags.WheeledDown) ? -1 : 0;
        var horizontalDelta = flags.HasFlag(MouseFlags.WheeledLeft)
            ? 1
            : flags.HasFlag(MouseFlags.WheeledRight) ? -1 : 0;
        if (verticalDelta == 0 && horizontalDelta == 0)
            return;
        if (commandMode is { IsOpen: true })
        {
            mouse.Handled = true;
            return;
        }

        var hasModifiers =
            flags.HasFlag(MouseFlags.Shift) ||
            flags.HasFlag(MouseFlags.Ctrl) ||
            flags.HasFlag(MouseFlags.Alt);
        mouse.Handled = true;
        TrackInputTask(DispatchMouseWheelAsync(
            horizontalDelta != 0 ? horizontalDelta : verticalDelta,
            hasModifiers,
            horizontalDelta != 0));
    }

    static void TrackInputTask(Task task)
        => _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    async Task DispatchKeyOrOpenCommandAsync(Key key, string initialText)
    {
        if (await DispatchKeyAsync(key))
            return;

        application.Invoke(() =>
        {
            if (window is not null &&
                commandMode is not null &&
                ReferenceEquals(application.TopRunnableView, window))
            {
                commandMode.Open(initialText, window.MostFocused);
            }
        });
    }

    static async Task<bool> DispatchKeyAsync(Key key)
    {
        try
        {
            return await HotkeyManager.HandleKeyAsync(key);
        }
        catch (Exception ex)
        {
            TerminalUi.LogException("URA", ex);
            TerminalUi.Notify("URA", ex.Message, UiSeverity.Error);
            return true;
        }
    }

    static async Task DispatchMouseWheelAsync(
        int delta,
        bool hasModifiers,
        bool horizontal)
    {
        try
        {
            await HotkeyManager.HandleMouseWheelAsync(delta, hasModifiers, horizontal);
        }
        catch (Exception ex)
        {
            TerminalUi.LogException("URA", ex);
            TerminalUi.Notify("URA", ex.Message, UiSeverity.Error);
        }
    }

    void RefreshNotificationOverlay()
    {
        if (window is null || notificationOverlay is null)
            return;

        var now = DateTimeOffset.Now;
        notificationOverlay.UpdateSnapshot(new(
            notifications
                .Where(notification =>
                    notification.Workspace is null ||
                    ReferenceEquals(notification.Workspace, renderedWorkspace))
                .Select(notification =>
                {
                    var (source, text) = SplitSource(notification.Text);
                    return new NotificationOverlayItem(
                        text,
                        notification.Severity,
                        notification.ExpiresAt,
                        source ?? notification.Workspace?.DisplayTitle);
                })
                .ToImmutableArray(),
            now,
            window.Viewport));
    }

    // Host messages are written "[source] text"; the card shows the source in its top border.
    static (string? Source, string Text) SplitSource(string text)
    {
        if (text.StartsWith('['))
        {
            var end = text.IndexOf("] ", StringComparison.Ordinal);
            if (end > 1)
                return (text[1..end], text[(end + 2)..]);
        }
        return (null, text);
    }

    void RefreshHotkeyOverlay()
    {
        if (window is null || hotkeyLayer is null)
            return;

        hotkeyLayer.Visible = false;
        if (hotkeyPopup is not null)
        {
            var lines = hotkeyPopup.Lines
                .Select((line, index) =>
                    hotkeyPopup.Selection?.SelectedLineIndex == index
                        ? $" › {line.Text}"
                        : $"   {line.Text}")
                .Skip(hotkeyPopup.ScrollOffset)
                .Take(Math.Max(1, window.Viewport.Height - 2))
                .ToArray();
            hotkeyLayer.Text = string.Join(Environment.NewLine, lines);
            hotkeyLayer.X = 0;
            hotkeyLayer.Y = Pos.AnchorEnd(Math.Max(1, lines.Length));
            hotkeyLayer.Width = Dim.Fill();
            hotkeyLayer.Height = Math.Max(1, lines.Length);
            hotkeyLayer.Visible = true;
        }
        hotkeyLayer.SetNeedsDraw();
    }

    void RemoveWorkspaceNotifications(Workspace workspace)
    {
        foreach (var notification in notifications
            .Where(notification => ReferenceEquals(notification.Workspace, workspace))
            .ToArray())
        {
            HotkeyManager.UnregisterNotificationShortcuts(
                notification.ShortcutRegistrationId);
        }
        notifications.RemoveAll(
            notification => ReferenceEquals(notification.Workspace, workspace));
    }

    sealed class UiHostWindow : Window
    {
        internal UiHostWindow(Action requestShutdown)
        {
            KeyBindings.Remove(Key.Enter);
            AddCommand(Command.Quit, () =>
            {
                requestShutdown();
                return true;
            });
        }
    }
}

[Flags]
internal enum UiChange
{
    None = 0,
    Workspace = 1,
    Notifications = 2,
    Hotkey = 4,
    Bootstrap = 8,
    All = Workspace | Notifications | Hotkey | Bootstrap
}
