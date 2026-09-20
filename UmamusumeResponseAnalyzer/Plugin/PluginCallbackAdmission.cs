using System.Collections.Immutable;

namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed class AnalyzerCallbackSnapshot(
    List<AnalyzerRegistration> items,
    List<IDisposable> leases) : IDisposable
{
    List<AnalyzerRegistration>? items = items;
    List<IDisposable>? leases = leases;

    internal int Count => items?.Count ?? 0;
    internal AnalyzerRegistration this[int index] => items![index];

    internal static AnalyzerCallbackSnapshot Create(
        ImmutableArray<AnalyzerRegistration> candidates,
        CancellationToken cancellationToken = default)
    {
        var admission = new Dictionary<IPlugin, bool>(ReferenceEqualityComparer.Instance);
        var items = new List<AnalyzerRegistration>();
        var leases = new List<IDisposable>();
        try
        {
            foreach (var candidate in candidates)
            {
                var owner = candidate.Plugin;
                if (!admission.TryGetValue(owner, out var admitted))
                {
                    var lease = PluginManager.TryEnterPluginCallback(owner, cancellationToken);
                    admitted = lease is not null;
                    admission.Add(owner, admitted);
                    if (lease is not null)
                        leases.Add(lease);
                }
                if (admitted)
                    items.Add(candidate);
            }

            return new(items, leases);
        }
        catch
        {
            for (var i = leases.Count - 1; i >= 0; i--)
                leases[i].Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref items, null)?.Clear();
        var currentLeases = Interlocked.Exchange(ref leases, null);
        if (currentLeases is null)
            return;

        for (var i = currentLeases.Count - 1; i >= 0; i--)
            currentLeases[i].Dispose();
        currentLeases.Clear();
    }
}

internal sealed class PluginGeneration(IPlugin plugin)
{
    static readonly AsyncLocal<bool> CallbackFlow = new();
    readonly object gate = new();
    readonly CancellationTokenSource backgroundCancellation = new();
    readonly HashSet<Task> backgroundTasks = [];
    PluginRegistrationPlan? pendingRegistration;
    TaskCompletionSource? callbackDrain;
    TaskCompletionSource? closeCompletion;
    int inFlight;
    bool initializing;
    bool accepting;
    bool closed;

    internal IPlugin Plugin { get; } = plugin;

    internal static bool HasActiveCallbackFlow
        => CallbackFlow.Value;

    internal bool IsAccepting
    {
        get
        {
            lock (gate)
                return accepting;
        }
    }

    internal IDisposable EnterInitialization()
    {
        lock (gate)
        {
            if (closed)
                throw Closed();
            if (initializing || accepting)
                throw new InvalidOperationException($"插件 generation 已在初始化或运行: {PluginManager.InternalName(Plugin)}");

            initializing = true;
            return EnterLocked();
        }
    }

    internal void CompleteInitialization()
    {
        lock (gate)
        {
            if (!initializing)
                throw new InvalidOperationException($"插件 generation 未在初始化: {PluginManager.InternalName(Plugin)}");

            initializing = false;
            if (closed)
                throw Closed();
        }
    }

    internal void AbortInitialization()
    {
        lock (gate)
        {
            initializing = false;
            pendingRegistration = null;
        }
    }

    internal void Open()
    {
        lock (gate)
        {
            if (closed)
                throw Closed();
            if (initializing)
                throw new InvalidOperationException($"插件 generation 仍在初始化: {PluginManager.InternalName(Plugin)}");
            accepting = true;
        }
    }

    internal bool TryStageRegistration(PluginRegistrationPlan plan)
    {
        lock (gate)
        {
            if (closed)
                throw Closed();
            if (!initializing)
                return false;
            if (pendingRegistration is not null)
                throw new InvalidOperationException(
                    $"插件 initialization registration 已提交: {PluginManager.InternalName(Plugin)}");

            pendingRegistration = plan;
            return true;
        }
    }

    internal PluginRegistrationPlan TakePendingRegistration()
    {
        lock (gate)
        {
            if (closed || initializing || accepting)
                throw Closed();
            var plan = pendingRegistration
                ?? throw new InvalidOperationException(
                    $"插件 initialization registration 尚未提交: {PluginManager.InternalName(Plugin)}");
            pendingRegistration = null;
            return plan;
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
            pendingRegistration = null;
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

            try
            {
                await callbackTask;
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }

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
                    "插件 generation 关闭失败。",
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

    internal IDisposable EnterCallbackFlow(IDisposable generationLease)
    {
        var previous = CallbackFlow.Value;
        if (!previous)
            CallbackFlow.Value = true;
        return new CallbackFlowLease(generationLease, previous);
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
            if (closed || !initializing && !accepting)
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
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var failure = new InvalidOperationException(
                $"插件后台操作失败: plugin={PluginManager.InternalName(Plugin)}, " +
                PluginManager.DescribeException(ex));
            _ = PluginManager.ReportPluginFailure("Plugin", failure, ex.ToString());
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
        => new($"插件 generation 已关闭或未开放: {PluginManager.InternalName(Plugin)}");

    sealed class Lease(Action release) : IDisposable
    {
        Action? release = release;

        public void Dispose()
            => Interlocked.Exchange(ref release, null)?.Invoke();
    }

    sealed class CallbackFlowLease(
        IDisposable generationLease,
        bool previous) : IDisposable
    {
        IDisposable? generationLease = generationLease;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref generationLease, null);
            if (current is null)
                return;
            if (CallbackFlow.Value != previous)
                CallbackFlow.Value = previous;
            current.Dispose();
        }
    }
}
