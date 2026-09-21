using System.Runtime.ExceptionServices;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using UmamusumeResponseAnalyzer.Commands;
using UmamusumeResponseAnalyzer.Plugin;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal sealed class UiHost : IUiInputSink, IDisposable
{
    readonly UiHostSession session = new();
    readonly UiHostSurface surface;
    // Exit does not wait for commands; an active command must still be able to release this semaphore.
    readonly SemaphoreSlim commandExecution = new(1, 1);
    readonly CancellationTokenRegistration lifetimeRegistration;

    int shutdownStarted;
    bool disposed;
    Exception? ingressFailure;

    internal UiHost(
        IApplication application,
        SynchronizationContext ownerContext,
        CancellationToken lifetimeToken)
    {
        Application = application ?? throw new ArgumentNullException(nameof(application));
        OwnerContext = ownerContext ?? throw new ArgumentNullException(nameof(ownerContext));
        LifetimeToken = lifetimeToken;
        surface = new(this, Application, session.Bootstrap);
        Bootstrap = new(this, session.Bootstrap);
        lifetimeRegistration = lifetimeToken.Register(
            static value => ((UiHost)value!).RequestShutdown(),
            this);
    }

    internal IApplication Application { get; }
    internal SynchronizationContext OwnerContext { get; }
    internal CancellationToken LifetimeToken { get; }
    internal BootstrapWorkspace Bootstrap { get; }
    internal Task Ready => surface.Ready;
    internal event Action<UiLogLine>? LogAdded
    {
        add => surface.LogAdded += value;
        remove => surface.LogAdded -= value;
    }
    internal event Action? ShutdownStarting;

    internal void EnsureAvailable() => session.EnsureAvailable();

    internal Workspace GetCurrentWorkspace() => session.GetCurrentWorkspace();

    internal Workspace CreateWorkspace(string title)
    {
        var workspace = session.CreateWorkspace(title, out var schedule);
        ScheduleDrain(schedule);
        return workspace;
    }

    internal void RemoveWorkspace(Workspace workspace)
    {
        if (!session.RemoveWorkspace(workspace, out var schedule))
            return;
        ScheduleDrain(schedule);
    }

    internal void SetPanel(
        Workspace workspace,
        string key,
        string title,
        WorkspaceContent content,
        bool fullBleed,
        bool switchToWorkspace)
    {
        var schedule = session.SetPanel(
            workspace,
            key,
            title,
            content,
            fullBleed,
            switchToWorkspace);
        ScheduleDrain(schedule);
    }

    internal bool RemovePanel(Workspace workspace, string key)
    {
        if (!session.RemovePanel(workspace, key, out var schedule))
            return false;
        ScheduleDrain(schedule);
        return true;
    }

    internal void Log(
        string text,
        UiSeverity severity,
        string? exceptionDetails = null)
    {
        var schedule = session.Log(text, severity, exceptionDetails);
        ScheduleDrain(schedule);
    }

    internal void Notify(
        Workspace? workspace,
        string text,
        UiSeverity severity,
        TimeSpan? ttl,
        UiShortcut[] shortcuts)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(shortcuts);
        session.EnsureWorkspaceAvailable(workspace);

        var expiresAt = DateTimeOffset.Now.Add(
            ttl ?? TimeSpan.FromSeconds(severity >= UiSeverity.Warning ? 10 : 5));
        var shortcutRegistrationId =
            HotkeyManager.RegisterNotificationShortcuts(expiresAt, shortcuts);
        try
        {
            var schedule = session.Notify(
                workspace,
                text,
                severity,
                expiresAt,
                shortcutRegistrationId);
            ScheduleDrain(schedule);
        }
        catch
        {
            HotkeyManager.UnregisterNotificationShortcuts(shortcutRegistrationId);
            throw;
        }
    }

    internal void SwitchWorkspace(Workspace workspace)
    {
        var schedule = session.SwitchWorkspace(workspace);
        ScheduleDrain(schedule);
    }

    internal void BindWorkspaceHotkey(
        Workspace workspace,
        ConsoleKey key,
        ConsoleModifiers modifiers,
        string? description)
    {
        var entry = HotkeyManager.CaptureTracked(
            description ?? $"切换到 {workspace.Title}",
            () =>
            {
                SwitchWorkspace(workspace);
                return Task.CompletedTask;
            });
        var schedule = session.BindWorkspaceHotkey(
            workspace,
            key,
            modifiers,
            entry);
        ScheduleDrain(schedule);
    }

    internal int RemoveWorkspaceHotkeysByOwner(object owner)
        => session.RemoveWorkspaceHotkeysByOwner(owner);

    internal Task FlushAsync()
    {
        var completion = session.Flush(out var schedule);
        ScheduleDrain(schedule);
        return completion;
    }

    internal Task HandleCommandAsync(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        session.EnsureAvailable();
        return QueueCommandAsync(command);
    }

    async Task QueueCommandAsync(string command)
    {
        await commandExecution.WaitAsync(LifetimeToken).ConfigureAwait(false);
        try
        {
            var snapshot = session.GetCommandSnapshot();
            await Task.Run(() => ExecuteCommandAsync(command, snapshot)).ConfigureAwait(false);
        }
        finally
        {
            commandExecution.Release();
        }
    }

    internal IReadOnlyList<string> CompleteCommand(string input)
        => HostCommands.Complete(input, session.GetCommandSnapshot() with
        {
            Plugins = PluginManager.SnapshotPluginStatuses()
        });

    internal void RequestShutdown()
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
            return;

        session.BeginShutdown();
        try
        {
            ShutdownStarting?.Invoke();
        }
        finally
        {
            OwnerContext.Post(
                static state => ((UiHostSurface)state!).RequestApplicationStop(),
                surface);
        }
    }

    internal async Task RunAsync()
    {
        session.ClaimRun();

        ExceptionDispatchInfo? primaryFailure = null;
        try
        {
            try
            {
                var plugins = await Task.Run(PluginManager.SnapshotPluginStatuses);
                session.SetPluginSnapshot(plugins);
                surface.Create();
            }
            catch (Exception ex)
            {
                var rollbackFailure = AbandonAll(session.FailRootCreation(ex), 0);
                if (rollbackFailure is not null)
                    throw new AggregateException(ex, rollbackFailure);
                throw;
            }

            var schedule = session.RootCreated();
            if (schedule)
                DrainPostedEvents();
            if (Volatile.Read(ref shutdownStarted) == 0)
            {
                surface.StartOverlayTimer(RefreshExpiringOverlays);
                await surface.RunAsync();
            }

            if (ingressFailure is not null)
                ExceptionDispatchInfo.Capture(ingressFailure).Throw();
        }
        catch (Exception ex)
        {
            var failure = ingressFailure is null || ReferenceEquals(ingressFailure, ex)
                ? ex
                : CombineFailure(ingressFailure, ex);
            primaryFailure = ExceptionDispatchInfo.Capture(failure);
        }

        Exception? cleanupFailure = null;
        try
        {
            Dispose();
        }
        catch (Exception ex)
        {
            cleanupFailure = ex;
        }

        if (primaryFailure is not null)
        {
            if (cleanupFailure is not null)
                throw new AggregateException(primaryFailure.SourceException, cleanupFailure);
            primaryFailure.Throw();
        }
        if (cleanupFailure is not null)
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Exception? failure = null;
        void Cleanup(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = CombineFailure(failure, ex);
            }
        }

        Cleanup(RequestShutdown);
        Cleanup(surface.StopOverlayTimer);
        Cleanup(DisconnectHotkeys);
        Cleanup(FinishRun);
        Cleanup(surface.Dispose);
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    int IUiInputSink.PopupVisibleLineCount
    {
        get
        {
            EnsureAvailable();
            return surface.PopupVisibleLineCount;
        }
    }

    Task<bool> IUiInputSink.TryHandleWorkspaceCommandAsync(Command command)
    {
        if (command is not (
            Command.Up or Command.Down or Command.PageUp or Command.PageDown or
            Command.Start or Command.End))
        {
            return Task.FromResult(false);
        }

        if (Environment.CurrentManagedThreadId == Application.MainThreadId)
        {
            EnsureAvailable();
            return Task.FromResult(surface.Navigate(command));
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var schedule = session.Navigate(command, completion);
        ScheduleDrain(schedule);
        return completion.Task;
    }

    void IUiInputSink.ShowPopup(HotkeyPopup popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        var schedule = session.ShowPopup(popup);
        ScheduleDrain(schedule);
    }

    void IUiInputSink.HidePopup()
    {
        var schedule = session.HidePopup();
        ScheduleDrain(schedule);
    }

    void ScheduleDrain(bool schedule)
    {
        if (schedule)
        {
            OwnerContext.Post(
                static state => _ = ((UiHost)state!).DrainPostedEvents(),
                this);
        }
    }

    bool DrainPostedEvents()
    {
        if (!session.BeginDrain())
            return false;

        try
        {
            var batch = DrainBatch();
            if (batch.Count == 0)
                return false;

            var failure = batch.Failure;
            try
            {
                surface.Reconcile(batch.Changes);
            }
            catch (Exception ex)
            {
                failure = CombineFailure(failure, ex);
            }

            foreach (var completion in batch.FlushCompletions)
            {
                if (failure is null)
                    completion.TrySetResult();
                else
                    completion.TrySetException(failure);
            }

            if (failure is not null)
            {
                ingressFailure = failure;
                RequestShutdown();
            }

        }
        finally
        {
            ScheduleDrain(session.CompleteDrain());
        }
        return false;
    }

    DrainBatchResult DrainBatch()
    {
        var changes = UiChange.None;
        var flushCompletions = new List<TaskCompletionSource>();
        var admittedEvents = session.TakeBatch();
        var failure = ingressFailure;
        if (failure is not null)
        {
            failure = AbandonAll(
                admittedEvents,
                0,
                failure,
                flushCompletions);
            return new(
                admittedEvents.Count,
                changes,
                flushCompletions,
                failure);
        }

        for (var index = 0; index < admittedEvents.Count; index++)
        {
            try
            {
                changes |= Apply(admittedEvents[index], flushCompletions);
            }
            catch (Exception ex)
            {
                failure = AbandonAll(
                    admittedEvents,
                    index,
                    ex,
                    flushCompletions)!;
                break;
            }
        }
        return new(admittedEvents.Count, changes, flushCompletions, failure);
    }

    UiChange Apply(
        UiHostIngress uiEvent,
        List<TaskCompletionSource> flushCompletions)
    {
        switch (uiEvent)
        {
            case RegisterWorkspaceIngress register:
                surface.RegisterWorkspace(register);
                return UiChange.Workspace;
            case RemoveWorkspaceIngress remove:
                surface.RemoveWorkspace(remove);
                var removedHotkey = session.RemoveWorkspaceHotkey(remove.Workspace);
                if (removedHotkey is not null)
                {
                    HotkeyManager.Unregister(
                        removedHotkey.Key,
                        removedHotkey.Modifiers,
                        removedHotkey.Entry);
                }
                return UiChange.All;
            case SetPanelIngress setPanel:
                surface.SetPanel(setPanel);
                return setPanel.SwitchToWorkspace ? UiChange.All : UiChange.Workspace;
            case RemovePanelIngress removePanel:
                surface.RemovePanel(removePanel);
                return UiChange.Workspace;
            case LogIngress log:
                surface.AddLog(log.Line);
                return UiChange.Workspace;
            case NotifyIngress notify:
                surface.AddNotification(notify);
                return UiChange.Notifications;
            case SwitchWorkspaceIngress switchWorkspace:
                surface.SwitchWorkspace(switchWorkspace.Workspace);
                return UiChange.All;
            case BindWorkspaceHotkeyIngress bindHotkey:
                using (var registration = bindHotkey.Entry.Owner is IPlugin plugin
                    ? PluginManager.TryEnterPluginRegistration(plugin)
                    : null)
                {
                    if (bindHotkey.Entry.Owner is IPlugin && registration is null)
                    {
                        session.CancelPendingWorkspaceHotkey(bindHotkey.Entry);
                        return UiChange.None;
                    }

                    if (!session.IsWorkspaceHotkeyPending(bindHotkey.Entry))
                        return UiChange.None;

                    var replaced = HotkeyManager.RegisterTracked(
                        bindHotkey.Key,
                        bindHotkey.Modifiers,
                        bindHotkey.Entry);
                    try
                    {
                        var shortcut = HotkeyManager.FormatKeyCombo(
                            bindHotkey.Key,
                            bindHotkey.Modifiers);
                        var commit = session.CommitWorkspaceHotkey(
                            bindHotkey,
                            replaced,
                            shortcut);
                        if (commit.Canceled)
                        {
                            HotkeyManager.RestoreTracked(
                                bindHotkey.Key,
                                bindHotkey.Modifiers,
                                bindHotkey.Entry,
                                replaced);
                            return UiChange.None;
                        }
                        if (commit.Previous is not null)
                        {
                            HotkeyManager.Unregister(
                                commit.Previous.Key,
                                commit.Previous.Modifiers,
                                commit.Previous.Entry);
                        }
                    }
                    catch
                    {
                        HotkeyManager.RestoreTracked(
                            bindHotkey.Key,
                            bindHotkey.Modifiers,
                            bindHotkey.Entry,
                            replaced);
                        throw;
                    }
                }
                return UiChange.None;
            case NavigateWorkspaceIngress navigate:
                navigate.Completion.TrySetResult(surface.Navigate(navigate.Command));
                return UiChange.None;
            case ShowPopupIngress showPopup:
                surface.ShowPopup(showPopup.Popup);
                return UiChange.Hotkey;
            case HidePopupIngress:
                surface.HidePopup();
                return UiChange.Hotkey;
            case FlushIngress flush:
                flushCompletions.Add(flush.Completion);
                return UiChange.None;
            default:
                throw new InvalidOperationException(
                    $"未知 UiHost ingress event: {uiEvent.GetType().FullName}");
        }
    }

    async Task ExecuteCommandAsync(
        string command,
        HostCommands.Snapshot snapshot)
    {
        HostCommands.Result? result = null;
        Exception? failure = null;
        try
        {
            snapshot = snapshot with
            {
                Plugins = PluginManager.InspectPluginStatuses()
            };
            result = await HostCommands.ExecuteAsync(
                command,
                snapshot,
                LifetimeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (LifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (LifetimeToken.IsCancellationRequested && failure is null)
            return;

        IReadOnlyList<PluginManager.PluginRuntimeStatus>? plugins = null;
        Exception? refreshFailure = null;
        if (!LifetimeToken.IsCancellationRequested)
        {
            try
            {
                plugins = PluginManager.InspectPluginStatuses();
            }
            catch (Exception ex)
            {
                refreshFailure = ex;
            }
        }

        Exception? postFailure = null;
        if (failure is not null)
        {
            try
            {
                TerminalUi.LogException("Command", failure);
                TerminalUi.Notify("Command", failure.Message, UiSeverity.Error);
            }
            catch (Exception ex)
            {
                postFailure = CombineFailure(failure, ex);
            }
        }

        if (LifetimeToken.IsCancellationRequested)
        {
            if (postFailure is not null)
                ExceptionDispatchInfo.Capture(postFailure).Throw();
            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
            return;
        }

        Exception? ownerFailure = null;
        try
        {
            await InvokeOnOwnerAsync(() =>
            {
                EnsureAvailable();
                try
                {
                    if (result is not null)
                        ApplyCommandResult(result);
                }
                finally
                {
                    if (plugins is not null)
                        session.SetPluginSnapshot(plugins);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ownerFailure = ex;
        }

        if (LifetimeToken.IsCancellationRequested &&
            postFailure is null &&
            failure is null &&
            refreshFailure is null)
            return;

        if (postFailure is null &&
            failure is not null &&
            (refreshFailure is not null ||
             ownerFailure is not null ||
             LifetimeToken.IsCancellationRequested))
        {
            postFailure = failure;
        }
        if (refreshFailure is not null)
            postFailure = CombineFailure(postFailure, refreshFailure);
        if (ownerFailure is not null)
            postFailure = CombineFailure(postFailure, ownerFailure);
        if (postFailure is not null)
            ExceptionDispatchInfo.Capture(postFailure).Throw();
    }

    Task InvokeOnOwnerAsync(Action action)
    {
        if (Environment.CurrentManagedThreadId == Application.MainThreadId)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        OwnerContext.Post(_ =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, null);
        return completion.Task;
    }

    internal void ApplyCommandResult(HostCommands.Result result)
    {
        if (!string.IsNullOrEmpty(result.Message))
        {
            Log(
                $"[Command] {result.Message}",
                result.Severity);
            if (result.Display is null)
            {
                Notify(
                    null,
                    $"[Command] {result.Message}",
                    result.Severity,
                    ttl: null,
                    shortcuts: []);
            }
        }
        if (result.Display is not null)
            surface.ShowCommandDisplay(result.Display);
        if (result.SwitchWorkspace is not null)
            SwitchWorkspace(result.SwitchWorkspace);
    }

    bool RefreshExpiringOverlays()
    {
        if (!session.IsRunning)
            return false;

        surface.ExpireNotifications();
        return session.IsRunning;
    }

    void DisconnectHotkeys()
    {
        if (!ReferenceEquals(HotkeyManager.OverlaySink, this))
            return;

        HotkeyManager.OverlaySink = null;
        HotkeyManager.UnregisterAll();
    }

    static Exception CombineFailure(Exception? failure, Exception next)
        => failure is null ? next : new AggregateException(failure, next);

    void FinishRun()
    {
        var finished = session.Finish();
        Exception? failure = AbandonAll(finished.Abandoned, 0, ingressFailure);
        if (ReferenceEquals(failure, ingressFailure))
            failure = null;

        void Capture(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception ex)
            {
                failure = CombineFailure(failure, ex);
            }
        }

        Capture(Bootstrap.Dispose);
        Capture(surface.Finish);
        foreach (var hotkey in finished.Hotkeys)
        {
            Capture(() => HotkeyManager.Unregister(
                hotkey.Key,
                hotkey.Modifiers,
                hotkey.Entry));
        }
        Capture(lifetimeRegistration.Dispose);
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    Exception? AbandonAll(
        IReadOnlyList<UiHostIngress> admittedEvents,
        int startIndex,
        Exception? failure = null,
        List<TaskCompletionSource>? deferredFlushCompletions = null)
    {
        var completionFailure = failure;
        for (var index = admittedEvents.Count - 1; index >= startIndex; index--)
        {
            try
            {
                var uiEvent = admittedEvents[index];
                if (deferredFlushCompletions is not null &&
                    uiEvent is FlushIngress flush)
                {
                    deferredFlushCompletions.Add(flush.Completion);
                    continue;
                }
                Abandon(uiEvent, completionFailure);
            }
            catch (Exception ex)
            {
                failure = CombineFailure(failure, ex);
            }
        }
        return failure;
    }

    void Abandon(UiHostIngress uiEvent, Exception? failure)
    {
        switch (uiEvent)
        {
            case NotifyIngress notify:
                HotkeyManager.UnregisterNotificationShortcuts(
                    notify.ShortcutRegistrationId);
                break;
            case NavigateWorkspaceIngress navigate:
                navigate.Completion.TrySetException(failure ?? new InvalidOperationException(
                    "UiHost stopped before the accepted navigation event was applied."));
                break;
            case FlushIngress flush:
                flush.Completion.TrySetException(failure ?? new InvalidOperationException(
                    "UiHost stopped before the accepted flush event was applied."));
                break;
            case BindWorkspaceHotkeyIngress bindHotkey:
                session.CancelPendingWorkspaceHotkey(bindHotkey.Entry);
                HotkeyManager.Unregister(
                    bindHotkey.Key,
                    bindHotkey.Modifiers,
                    bindHotkey.Entry);
                break;
        }
    }

    sealed record DrainBatchResult(
        int Count,
        UiChange Changes,
        List<TaskCompletionSource> FlushCompletions,
        Exception? Failure);

}
