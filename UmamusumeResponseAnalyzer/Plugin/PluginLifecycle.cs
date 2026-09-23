using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;

namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed class PluginLifecycle(IPlugin plugin)
{
    readonly object gate = new();
    TaskCompletionSource? callbackDrain;
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
                throw new InvalidOperationException(string.Format(i18n.LifecycleAlreadyRunning, PluginManager.InternalName(Plugin)));

            initializing = true;
            return EnterLocked();
        }
    }

    internal void CompleteInitialization()
    {
        lock (gate)
        {
            if (!initializing)
                throw new InvalidOperationException(string.Format(i18n.LifecycleNotInitializing, PluginManager.InternalName(Plugin)));

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
                throw new InvalidOperationException(string.Format(i18n.LifecycleStillInitializing, PluginManager.InternalName(Plugin)));
            accepting = true;
        }
    }

    internal Task Close()
    {
        lock (gate)
        {
            accepting = false;
            closed = true;
            return inFlight == 0
                ? Task.CompletedTask
                : (callbackDrain ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
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

    internal void ValidateRegistrationCommit()
    {
        lock (gate)
        {
            if (closed || !accepting)
                throw Closed();
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
        => new(string.Format(i18n.LifecycleUnavailable, PluginManager.InternalName(Plugin)));

    sealed class Lease(Action release) : IDisposable
    {
        Action? release = release;

        public void Dispose()
            => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
