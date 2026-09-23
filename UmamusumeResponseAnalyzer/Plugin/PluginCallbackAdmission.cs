using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;

namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed class PluginGeneration(IPlugin plugin)
{
    readonly object gate = new();
    readonly CancellationTokenSource backgroundCancellation = new();
    readonly List<Task> backgroundTasks = [];
    TaskCompletionSource? callbackDrain;
    TaskCompletionSource? closeCompletion;
    int inFlight;
    bool initializing;
    bool accepting;
    bool closed;
    string? failure;

    internal IPlugin Plugin { get; } = plugin;
    internal Task? CleanupTask { get; set; }

    internal string? Failure
    {
        get
        {
            lock (gate)
                return failure;
        }
    }

    internal bool IsFaulted => Failure is not null;

    internal bool TryMarkFaulted(string reason)
    {
        lock (gate)
        {
            if (closed)
                return false;

            failure = reason;
            accepting = false;
            closed = true;
            return true;
        }
    }

    internal bool IsAccepting
    {
        get
        {
            lock (gate)
                return accepting;
        }
    }

    internal bool IsClosed
    {
        get
        {
            lock (gate)
                return closed;
        }
    }

    internal IDisposable EnterInitialization()
    {
        lock (gate)
        {
            if (closed)
                throw Closed();
            if (initializing || accepting)
                throw new InvalidOperationException(string.Format(i18n.GenerationAlreadyRunning, PluginManager.InternalName(Plugin)));

            initializing = true;
            return EnterLocked();
        }
    }

    internal void CompleteInitialization()
    {
        lock (gate)
        {
            if (!initializing)
                throw new InvalidOperationException(string.Format(i18n.GenerationNotInitializing, PluginManager.InternalName(Plugin)));

            initializing = false;
            if (closed)
                throw Closed();
        }
    }

    internal void AbortInitialization()
    {
        lock (gate)
            initializing = false;
    }

    internal void Open()
    {
        lock (gate)
        {
            if (closed)
                throw Closed();
            if (initializing)
                throw new InvalidOperationException(string.Format(i18n.GenerationStillInitializing, PluginManager.InternalName(Plugin)));
            accepting = true;
        }
    }

    internal Task Close()
    {
        Task callbackTask;
        Task[] backgroundSnapshot;
        TaskCompletionSource completion;
        lock (gate)
        {
            if (closeCompletion is not null)
                return closeCompletion.Task;

            accepting = false;
            closed = true;
            callbackTask = inFlight == 0
                ? Task.CompletedTask
                : (callbackDrain = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            backgroundSnapshot = [.. backgroundTasks];
            backgroundTasks.Clear();
            completion = closeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = CompleteCloseAsync(callbackTask, backgroundSnapshot, completion);
        return completion.Task;
    }

    async Task CompleteCloseAsync(
        Task callbackTask,
        Task[] backgroundSnapshot,
        TaskCompletionSource completion)
    {
        List<Exception> failures = [];
        try
        {
            try
            {
                await backgroundCancellation.CancelAsync();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }

            await callbackTask;

            try
            {
                await Task.WhenAll(backgroundSnapshot);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }

            if (failures.Count == 0)
                completion.TrySetResult();
            else if (failures.Count == 1)
                completion.TrySetException(failures[0]);
            else
                completion.TrySetException(new AggregateException(
                    i18n.GenerationCloseFailed,
                    failures));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            backgroundCancellation.Dispose();
        }
    }

    internal bool TryEnterCallback(out IDisposable? lease)
    {
        lock (gate)
        {
            if (!accepting)
            {
                lease = null;
                return false;
            }

            lease = EnterLocked();
            return true;
        }
    }

    internal bool TryEnterInspection(out IDisposable? lease)
    {
        lock (gate)
        {
            if (closed)
            {
                lease = null;
                return false;
            }

            lease = EnterLocked();
            return true;
        }
    }

    internal bool TryEnterRegistration(out IDisposable? lease)
    {
        lock (gate)
        {
            if (closed || !initializing && !accepting)
            {
                lease = null;
                return false;
            }

            lease = EnterLocked();
            return true;
        }
    }

    internal void ValidateBackgroundAdmission()
    {
        lock (gate)
        {
            if (closed || !accepting)
                throw Closed();
        }
    }

    internal void RunBackground(IReadOnlyList<Func<CancellationToken, ValueTask>> operations)
    {
        lock (gate)
        {
            if (closed || !accepting)
                throw Closed();
            foreach (var operation in operations)
                backgroundTasks.Add(Task.Run(() => InvokeBackgroundAsync(operation, backgroundCancellation.Token)));
        }
    }

    async Task InvokeBackgroundAsync(
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken)
    {
        if (IsFaulted)
            return;

        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (PluginManager.TryDisableForMissingAssembly(Plugin, ex, "Background"))
                return;

            var failure = new InvalidOperationException(
                string.Format(i18n.BackgroundOperationFailed, PluginManager.InternalName(Plugin),
                    PluginManager.DescribeException(ex)));
            PluginManager.ReportPluginFailure("Plugin", failure, ex.ToString());
        }
    }

    IDisposable EnterLocked()
    {
        inFlight++;
        return new Lease(Exit);
    }

    void Exit()
    {
        TaskCompletionSource? completion = null;
        lock (gate)
        {
            if (--inFlight == 0)
                completion = callbackDrain;
        }
        completion?.TrySetResult();
    }

    InvalidOperationException Closed()
        => new(string.Format(i18n.GenerationUnavailable, PluginManager.InternalName(Plugin)));

    sealed class Lease(Action release) : IDisposable
    {
        Action? release = release;

        public void Dispose()
            => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
