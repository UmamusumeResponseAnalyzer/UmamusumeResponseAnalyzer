using System.Threading.Channels;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    public sealed class UiHost : IKeyboardOverlaySink
    {
        const int MaxLogLines = 300;

        readonly Channel<UiEvent> events = CreateUiChannel<UiEvent>();
        readonly UiRefreshSignal refreshSignal = new();
        readonly NotificationPopupRenderer popupRenderer = new();
        readonly WorkspaceLayoutBuilder layoutBuilder = new();

        readonly Dictionary<LiveDisplayWorkspace, WorkspaceState> workspaces = [];
        readonly Dictionary<(LiveDisplayWorkspace Workspace, string PluginId, string Key), LiveDisplayPanel> panels = [];
        readonly List<LiveDisplayLogLine> logs = [];
        readonly List<LiveDisplayNotification> notifications = [];
        readonly Channel<ConsoleInteractionRequest> consoleInteractions = CreateUiChannel<ConsoleInteractionRequest>();

        LiveDisplayWorkspace? activeWorkspace;
        KeyboardPopup? keyboardPopup;
        bool shutdownRequested;
        int runState;
        int acceptingEvents = 1;

        public ILiveDisplayOutput ForPlugin(string pluginId) => new PluginLiveDisplayOutput(pluginId, this);

        public LiveDisplayWorkspace CreateWorkspace(string id, string title)
        {
            var workspace = LiveDisplayWorkspace.Create(id, title);
            RegisterWorkspace(workspace);
            return workspace;
        }

        public void RegisterWorkspace(LiveDisplayWorkspace workspace) => Post(new UiEvent.RegisterWorkspace(workspace));
        public void SetPanel(LiveDisplayPanel panel) => Post(new UiEvent.SetPanel(panel));
        public void Log(LiveDisplayLogLine line) => Post(new UiEvent.Log(line));
        public void Notify(LiveDisplayNotification notification) => Post(new UiEvent.Notify(notification));
        public void SwitchWorkspace(LiveDisplayWorkspace workspace) => Post(new UiEvent.SwitchWorkspace(workspace));
        public void BindWorkspaceHotkey(
            LiveDisplayWorkspace workspace,
            ConsoleKey key,
            ConsoleModifiers modifiers = 0,
            string? description = null)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var shortcutText = KeyboardManager.FormatKeyCombo(key, modifiers);
            KeyboardManager.Register(
                key,
                modifiers,
                description ?? $"切换到 {workspace.Title}",
                () =>
                {
                    SwitchWorkspace(workspace);
                    return Task.CompletedTask;
                });
            Post(new UiEvent.RegisterWorkspace(workspace));
            Post(new UiEvent.SetWorkspaceShortcut(workspace, shortcutText));
        }

        public void RequestShutdown() => Post(new UiEvent.Shutdown());
        public void HidePopup() => Post(new UiEvent.HidePopup());
        void IKeyboardOverlaySink.ShowPopup(KeyboardPopup popup) => Post(new UiEvent.ShowPopup(popup));
        internal bool IsRunning => Volatile.Read(ref runState) != 0;

        internal void RenderSnapshot(IAnsiConsole console)
        {
            if (IsRunning)
                throw new InvalidOperationException("RenderSnapshot 只能在 UiHost 未运行时用于测试或诊断。");

            DrainEvents();
            RemoveExpiredNotifications(DateTimeOffset.Now);
            console.Write(BuildLayout(console.Profile.Width, console.Profile.Height));
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref runState, 1) != 0)
                throw new InvalidOperationException("UiHost.RunAsync 已在运行中。");

            try
            {
                if (!HasInteractiveConsole())
                {
                    await RunHeadlessAsync(cancellationToken);
                    return;
                }

                while (!cancellationToken.IsCancellationRequested && !shutdownRequested)
                {
                    var request = await RunLiveDisplayUntilConsoleInteractionAsync(cancellationToken);
                    if (request is null)
                        continue;

                    await ExecuteConsoleInteractionAsync(request, clearConsole: true);
                }
            }
            finally
            {
                Volatile.Write(ref runState, 0);
                Volatile.Write(ref acceptingEvents, 0);
                events.Writer.TryComplete();
                consoleInteractions.Writer.TryComplete();
                FailPendingConsoleInteractions();
            }
        }

        async Task<ConsoleInteractionRequest?> RunLiveDisplayUntilConsoleInteractionAsync(CancellationToken cancellationToken)
        {
            ConsoleInteractionRequest? pendingInteraction = null;
            var width = GetConsoleWidth();
            var height = GetConsoleHeight();
            await AnsiConsole.Live(BuildLayout(width, height))
                .Overflow(VerticalOverflow.Crop)
                .Cropping(VerticalOverflowCropping.Bottom)
                .StartAsync(async ctx =>
                {
                    while (!cancellationToken.IsCancellationRequested && !shutdownRequested)
                    {
                        if (consoleInteractions.Reader.TryRead(out pendingInteraction))
                            break;

                        var changed = DrainEvents();
                        var now = DateTimeOffset.Now;
                        if (RemoveExpiredNotifications(now))
                            changed = true;
                        if (popupRenderer.ShouldRefreshPopupCountdown(notifications, keyboardPopup, now))
                            changed = true;

                        var currentWidth = GetConsoleWidth();
                        var currentHeight = GetConsoleHeight();
                        if (currentWidth != width || currentHeight != height)
                        {
                            width = currentWidth;
                            height = currentHeight;
                            changed = true;
                        }

                        if (changed)
                            ctx.UpdateTarget(BuildLayout(width, height));

                        try
                        {
                            await refreshSignal.WaitForAsync(cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                });

            return pendingInteraction;
        }

        async Task RunHeadlessAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && !shutdownRequested)
            {
                DrainEvents();
                var now = DateTimeOffset.Now;
                RemoveExpiredNotifications(now);
                while (consoleInteractions.Reader.TryRead(out var interaction))
                    await ExecuteConsoleInteractionAsync(interaction, clearConsole: false);

                try { await refreshSignal.WaitForAsync(cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        internal Task RunConsoleInteractionAsync(Func<Task> action)
        {
            if (!IsRunning)
                return action();

            var request = new ConsoleInteractionRequest(action);
            if (!consoleInteractions.Writer.TryWrite(request))
                throw new InvalidOperationException("Console interaction queue is closed.");

            refreshSignal.Signal();
            return request.Completion;
        }

        static async Task ExecuteConsoleInteractionAsync(ConsoleInteractionRequest request, bool clearConsole)
        {
            using var inputSuspension = KeyboardManager.SuspendInput();
            using var interactionScope = LiveDisplayConsole.EnterConsoleInteraction();
            try
            {
                if (clearConsole)
                    AnsiConsole.Clear();

                await request.Action();
                request.SetResult();
            }
            catch (Exception ex)
            {
                request.SetException(ex);
            }
            finally
            {
                if (clearConsole)
                    AnsiConsole.Clear();
            }
        }

        void FailPendingConsoleInteractions()
        {
            while (consoleInteractions.Reader.TryRead(out var interaction))
                interaction.SetException(new OperationCanceledException("LiveDisplay 已停止，无法执行 console interaction。"));
        }

        void Post(UiEvent uiEvent)
        {
            if (Volatile.Read(ref acceptingEvents) == 0)
                return;

            if (!events.Writer.TryWrite(uiEvent))
                return;

            refreshSignal.Signal();
        }

        bool DrainEvents()
        {
            var changed = false;
            while (events.Reader.TryRead(out var uiEvent))
            {
                changed = true;
                Apply(uiEvent);
            }
            return changed;
        }

        void Apply(UiEvent uiEvent)
        {
            switch (uiEvent)
            {
                case UiEvent.RegisterWorkspace registerWorkspace:
                    RegisterKnownWorkspace(registerWorkspace.Workspace);
                    break;
                case UiEvent.SetWorkspaceShortcut setWorkspaceShortcut:
                    SetWorkspaceShortcut(setWorkspaceShortcut.Workspace, setWorkspaceShortcut.ShortcutText);
                    break;
                case UiEvent.SetPanel setPanel:
                    RegisterKnownWorkspace(setPanel.Panel.Workspace);
                    panels[(setPanel.Panel.Workspace, setPanel.Panel.PluginId, setPanel.Panel.Key)] = setPanel.Panel;
                    break;
                case UiEvent.Log log:
                    if (log.Line.Workspace is not null)
                        RegisterKnownWorkspace(log.Line.Workspace);
                    logs.Add(log.Line);
                    if (logs.Count > MaxLogLines)
                        logs.RemoveRange(0, logs.Count - MaxLogLines);
                    break;
                case UiEvent.Notify notify:
                    if (notify.Notification.Workspace is not null)
                        RegisterKnownWorkspace(notify.Notification.Workspace);
                    notifications.Add(notify.Notification);
                    break;
                case UiEvent.SwitchWorkspace switchWorkspace:
                    RegisterKnownWorkspace(switchWorkspace.Workspace);
                    activeWorkspace = switchWorkspace.Workspace;
                    break;
                case UiEvent.ShowPopup showPopup:
                    keyboardPopup = showPopup.Popup;
                    break;
                case UiEvent.HidePopup:
                    keyboardPopup = null;
                    break;
                case UiEvent.Shutdown:
                    shutdownRequested = true;
                    break;
            }
        }

        IRenderable BuildLayout(int width, int height)
        {
            if (width <= 0)
                width = 120;
            if (height <= 0)
                height = 35;

            IRenderable content = layoutBuilder.BuildWorkspaceLayout(
                new WorkspaceLayoutBuilder.State(activeWorkspace, panels.Values, logs, WorkspaceLabel),
                width,
                height);
            var popupWidth = NotificationPopupRenderer.GetPopupWidth(width);
            var now = DateTimeOffset.Now;
            if (popupWidth > 0)
            {
                var activeNotifications = GetActiveNotifications();
                if (activeNotifications.Count > 0)
                {
                    content = new NotificationOverlayRenderable(
                        content,
                        popupRenderer.BuildLines(activeNotifications, popupWidth, Math.Max(0, height - 1), now, WorkspaceLabel),
                        popupWidth);
                }
            }

            if (keyboardPopup is not null)
                content = new KeyboardPopupOverlayRenderable(content, keyboardPopup, width, height, bottomInset: 0, now: now);

            return content;
        }

        List<LiveDisplayNotification> GetActiveNotifications()
        {
            return notifications
                .OrderByDescending(x => x.ExpiresAt)
                .ToList();
        }

        internal IReadOnlyList<string> BuildNotificationPopupPreview(int width)
        {
            DrainEvents();
            var now = DateTimeOffset.Now;
            RemoveExpiredNotifications(now);
            var activeNotifications = GetActiveNotifications();
            var popupWidth = NotificationPopupRenderer.GetPopupWidth(width);
            if (popupWidth == 0 || activeNotifications.Count == 0)
                return [];

            return popupRenderer.BuildLines(activeNotifications, popupWidth, int.MaxValue, now, WorkspaceLabel);
        }

        string WorkspaceLabel(LiveDisplayWorkspace workspace)
        {
            return workspaces.TryGetValue(workspace, out var state) && !string.IsNullOrEmpty(state.ShortcutText)
                ? $"{state.ShortcutText} {workspace.Title}"
                : workspace.Title;
        }

        static Channel<T> CreateUiChannel<T>()
        {
            return Channel.CreateUnbounded<T>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        }

        bool RemoveExpiredNotifications(DateTimeOffset now)
        {
            return notifications.RemoveAll(x => x.ExpiresAt <= now) > 0;
        }

        void RegisterKnownWorkspace(LiveDisplayWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            if (workspaces.ContainsKey(workspace))
            {
                activeWorkspace ??= workspace;
                return;
            }

            workspaces[workspace] = new WorkspaceState(ShortcutText: null);
            activeWorkspace ??= workspace;
        }

        void SetWorkspaceShortcut(LiveDisplayWorkspace workspace, string shortcutText)
        {
            RegisterKnownWorkspace(workspace);
            var state = workspaces[workspace];
            workspaces[workspace] = state with { ShortcutText = shortcutText };
        }

        static int GetConsoleWidth()
        {
            try { return Console.WindowWidth; }
            catch { return 120; }
        }

        static int GetConsoleHeight()
        {
            try { return Console.WindowHeight; }
            catch { return 35; }
        }

        static bool HasInteractiveConsole()
        {
            if (Console.IsOutputRedirected)
                return false;

            try
            {
                _ = Console.WindowWidth;
                _ = Console.WindowHeight;
                return true;
            }
            catch
            {
                return false;
            }
        }

        sealed record WorkspaceState(string? ShortcutText);

        sealed class ConsoleInteractionRequest(Func<Task> action)
        {
            readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Func<Task> Action => action;
            public Task Completion => completion.Task;

            public void SetResult()
            {
                completion.TrySetResult();
            }

            public void SetException(Exception exception)
            {
                completion.TrySetException(exception);
            }
        }
    }
}
