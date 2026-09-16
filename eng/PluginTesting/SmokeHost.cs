using System.Globalization;
using UmamusumeResponseAnalyzer;
using Terminal.Gui.ViewBase;
using System.Collections.Concurrent;
using Gallop.Endpoints;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Time;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

sealed class WorkspaceSmokeSession : IDisposable
{
    readonly BlockingCollection<RunRequest> runs = [];
    readonly CancellationTokenSource lifetime = new();
    readonly TaskCompletionSource<Startup> ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Thread uiThread;
    readonly int width;
    readonly int height;
    readonly UiHost host;
    readonly Task run;
    IApplication? ownedApplication;
    BootstrapWorkspace? ownedBootstrap;
    int disposed;

    public WorkspaceSmokeSession(int width = 120, int height = 40)
    {
        InitializeConfig();
        this.width = width;
        this.height = height;
        uiThread = new Thread(RunUiThread)
        {
            IsBackground = true,
            Name = "URA workspace smoke"
        };
        uiThread.Start();

        var startup = ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Application = startup.Application;
        host = startup.Host;
        Bootstrap = startup.Bootstrap;
        run = StartAsync(host.RunAsync).GetAwaiter().GetResult();
        if (run.IsCompleted)
        {
            run.GetAwaiter().GetResult();
            throw new InvalidOperationException("UiHost stopped during workspace smoke startup.");
        }
        Flush();
    }

    static void InitializeConfig()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var configDirectory = Path.Combine(
            Path.GetTempPath(),
            "ura-workspace-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDirectory);
        try
        {
            Directory.SetCurrentDirectory(configDirectory);
            UmamusumeResponseAnalyzer.Config.Initialize();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    public IApplication Application { get; }
    public Workspace Bootstrap { get; }

    public void Flush()
        => host.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

    public Task RunDialogAsync(Func<Task> action, Action<Dialog> interact)
        => InvokeOnOwnerAsync(() =>
        {
            Application.AddTimeout(TimeSpan.Zero, () =>
            {
                if (Application.TopRunnableView is not Dialog dialog)
                    return true;

                interact(dialog);
                return false;
            });
            action().GetAwaiter().GetResult();
        });

    public void SendKey(Key key)
        => InvokeOnOwnerAsync(() =>
        {
            Application.Keyboard.RaiseKeyDownEvent(key);
            Application.LayoutAndDraw(forceRedraw: true);
        }).GetAwaiter().GetResult();

    public bool WasHandledByApplicationKeyDown(Key key)
        => InvokeOnOwner(() =>
        {
            var handled = false;
            void Observe(object? _, Key observed) => handled = observed.Handled;

            Application.Keyboard.KeyDown += Observe;
            try
            {
                Application.Keyboard.RaiseKeyDownEvent(key);
                Application.LayoutAndDraw(forceRedraw: true);
            }
            finally
            {
                Application.Keyboard.KeyDown -= Observe;
            }

            return handled;
        });

    public void SendMouse(Mouse mouse)
        => InvokeOnOwnerAsync(() =>
        {
            Application.Mouse.RaiseMouseEvent(mouse);
            Application.LayoutAndDraw(forceRedraw: true);
        }).GetAwaiter().GetResult();

    public string CaptureScreen()
        => CaptureScreen(width, height);

    public string CaptureScreen(
        int screenWidth,
        int screenHeight,
        bool restore = true,
        bool flushHost = true)
    {
        if (flushHost)
            Flush();
        return InvokeOnOwner(() =>
        {
            var driver = Application.Driver
                ?? throw new InvalidOperationException("Terminal.Gui driver was not initialized.");
            var originalSize = driver.Screen.Size;
            var requestedSize = new System.Drawing.Size(screenWidth, screenHeight);
            var resized = originalSize != requestedSize;
            if (resized)
                driver.SetScreenSize(screenWidth, screenHeight);
            Application.LayoutAndDraw(forceRedraw: true);
            var screen = driver.ToString();
            if (restore && resized)
            {
                driver.SetScreenSize(originalSize.Width, originalSize.Height);
                Application.LayoutAndDraw(forceRedraw: true);
            }
            return screen;
        });
    }

    public Cell[,] CaptureCells()
        => CaptureCells(width, height);

    public Cell[,] CaptureCells(int screenWidth, int screenHeight)
    {
        Flush();
        return InvokeOnOwner(() =>
        {
            var driver = Application.Driver
                ?? throw new InvalidOperationException("Terminal.Gui driver was not initialized.");
            if (driver.Screen.Size != new System.Drawing.Size(screenWidth, screenHeight))
                driver.SetScreenSize(screenWidth, screenHeight);
            Application.LayoutAndDraw(forceRedraw: true);
            return (Cell[,])(driver.Contents?.Clone()
                ?? throw new InvalidOperationException("Terminal.Gui framebuffer was not initialized."));
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        try
        {
            lifetime.Cancel();
            run.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        finally
        {
            runs.CompleteAdding();
            if (!uiThread.Join(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Workspace smoke UI thread did not stop.");
            runs.Dispose();
            lifetime.Dispose();
        }
    }

    async Task<Task> StartAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runs.Add(new(action, completion));
        await WaitForAsync(() => Application.TopRunnableView is not null || completion.Task.IsCompleted);
        if (completion.Task.IsCompleted)
            return completion.Task;

        await WaitForAsync(() => Application.Driver?.SixelSupport is not null);
        await InvokeOnOwnerAsync(() =>
        {
            var driver = Application.Driver
                ?? throw new InvalidOperationException("Terminal.Gui driver was not initialized.");
            _ = driver.SixelSupport
                ?? throw new InvalidOperationException("Sixel detection did not complete.");
            driver.SetSixelSupport(new()
            {
                IsSupported = true,
                Resolution = new(10, 20)
            });
            driver.SetScreenSize(width, height);
            Application.LayoutAndDraw(forceRedraw: true);
        });
        return completion.Task;
    }

    async Task WaitForAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(10, cancellation.Token);
    }

    public T InvokeOnOwner<T>(Func<T> action)
    {
        T result = default!;
        InvokeOnOwnerAsync(() => result = action()).GetAwaiter().GetResult();
        return result;
    }

    async Task InvokeOnOwnerAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Application.Invoke(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    void RunUiThread()
    {
        var previousContext = SynchronizationContext.Current;
        var previousDisableRealDriverIo = Environment.GetEnvironmentVariable("DisableRealDriverIO");
        try
        {
            Environment.SetEnvironmentVariable("DisableRealDriverIO", "1");
            var application = Terminal.Gui.App.Application.Create(new VirtualTimeProvider())
                .Init(DriverRegistry.Names.ANSI);
            ownedApplication = application;
            application.Driver!.SetScreenSize(width, height);
            var context = new OwnerSynchronizationContext(this);
            SynchronizationContext.SetSynchronizationContext(context);
            application.Iteration += ApplicationIteration;
            var createdHost = new UiHost(application, context, lifetime.Token);
            TerminalUi.Initialize(createdHost);
            ownedBootstrap = createdHost.Bootstrap;
            ready.SetResult(new(application, createdHost, ownedBootstrap.Workspace));
            foreach (var request in runs.GetConsumingEnumerable())
                request.Execute();
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
            runs.CompleteAdding();
            while (runs.TryTake(out var request))
                request.Fail(ex);
        }
        finally
        {
            try
            {
                ownedBootstrap?.Dispose();
                ownedBootstrap = null;
                if (ownedApplication is { } application)
                    application.Iteration -= ApplicationIteration;
                ownedApplication?.Dispose();
                ownedApplication = null;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
                Environment.SetEnvironmentVariable("DisableRealDriverIO", previousDisableRealDriverIo);
            }
        }
    }

    Task DispatchToOwner(SendOrPostCallback callback, object? state)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new RunRequest(
            () =>
            {
                callback(state);
                return Task.CompletedTask;
            },
            completion);
        try
        {
            runs.Add(request);
            var application = Volatile.Read(ref ownedApplication)
                ?? throw new ObjectDisposedException(nameof(WorkspaceSmokeSession));
            application.Invoke(static () => { });
        }
        catch (Exception ex)
        {
            request.Fail(ex);
            throw;
        }
        return completion.Task;
    }

    void ApplicationIteration(
        object? sender,
        Terminal.Gui.App.EventArgs<IApplication?> e)
    {
        while (runs.TryTake(out var request))
            request.Execute();
    }

    sealed record Startup(IApplication Application, UiHost Host, Workspace Bootstrap);

    sealed class OwnerSynchronizationContext(WorkspaceSmokeSession owner) : SynchronizationContext
    {
        readonly int ownerThreadId = Environment.CurrentManagedThreadId;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _ = owner.DispatchToOwner(callback, state);
        }

        public override void Send(SendOrPostCallback callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (Environment.CurrentManagedThreadId == ownerThreadId)
            {
                callback(state);
                return;
            }

            owner.DispatchToOwner(callback, state).GetAwaiter().GetResult();
        }

        public override SynchronizationContext CreateCopy() => this;
    }

    sealed class RunRequest(Func<Task> action, TaskCompletionSource completion)
    {
        int started;

        public TaskCompletionSource Completion { get; } = completion;

        public void Fail(Exception exception)
        {
            if (Interlocked.Exchange(ref started, 1) == 0)
                Completion.TrySetException(exception);
        }

        public void Execute()
        {
            if (Interlocked.Exchange(ref started, 1) != 0)
                return;

            try
            {
                var task = action();
                if (task.IsCompleted)
                {
                    Complete(task);
                    return;
                }

                _ = task.ContinueWith(
                    static (completed, state) => ((RunRequest)state!).Complete(completed),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                Completion.TrySetException(ex);
            }
        }

        void Complete(Task task)
        {
            try
            {
                task.GetAwaiter().GetResult();
                Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                Completion.TrySetException(ex);
            }
        }
    }
}

sealed class RecordingHostEvents : IPluginHostEvents, IDisposable
{
    bool disposed;
    public List<RecordedHostSubscription> Subscriptions { get; } = [];

    public void OnStarted(Func<CancellationToken, ValueTask> handler)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Subscriptions.Add(new(handler));
    }

    public void Dispose()
    {
        disposed = true;
        foreach (var subscription in Subscriptions)
            subscription.Dispose();
    }
}

sealed class RecordedHostSubscription(Func<CancellationToken, ValueTask> handler) : IDisposable
{
    public Func<CancellationToken, ValueTask> Handler { get; } = handler;
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

sealed class RecordingAnalyzerRegistry : IPluginAnalyzerRegistry, IDisposable
{
    bool disposed;
    public List<RecordedAnalyzerRegistration> Registrations { get; } = [];

    public void Register<TPayload>(
        AnalyzerKind kind,
        IReadOnlyList<EndpointPattern> patterns,
        Func<AnalyzerInvocation<TPayload>, ValueTask> handler,
        int priority = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Registrations.Add(new(
            kind,
            [.. patterns],
            typeof(TPayload),
            priority,
            (endpoint, payload, headers) => handler(new(endpoint, (TPayload)payload, headers))));
    }

    public void Dispose()
    {
        disposed = true;
        foreach (var registration in Registrations)
            registration.Dispose();
    }

}

sealed class RecordedAnalyzerRegistration(
    AnalyzerKind kind,
    IReadOnlyList<EndpointPattern> patterns,
    Type payloadType,
    int priority,
    Func<GameEndpointDescriptor, object, GameHttpHeaders, ValueTask> handler) : IDisposable
{
    public AnalyzerKind Kind { get; } = kind;
    public IReadOnlyList<EndpointPattern> Patterns { get; } = patterns;
    public Type PayloadType { get; } = payloadType;
    public int Priority { get; } = priority;
    public bool IsDisposed { get; private set; }

    public ValueTask Invoke(AnalyzerDispatchContext context)
        => Invoke(
            PayloadType == typeof(ReadOnlyMemory<byte>) ? context.Payload : context.GetDto(PayloadType),
            context.Endpoint,
            context.Headers);

    public ValueTask Invoke(
        object payload,
        GameEndpointDescriptor? endpoint = null,
        GameHttpHeaders? headers = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        endpoint ??= ResolveEndpoint();
        headers ??= new(null, null, null, null, null, null);
        return handler(endpoint, payload, headers);
    }

    GameEndpointDescriptor ResolveEndpoint()
    {
        var expanded = PluginManager.ExpandEndpointPatterns(Patterns);
        return expanded.FirstOrDefault(candidate =>
                   (Kind == AnalyzerKind.Request ? candidate.RequestType : candidate.ResponseType) == PayloadType)
               ?? expanded[0];
    }

    public void Dispose() => IsDisposed = true;
}

sealed class RuntimePluginContext(
    IApplication application,
    IReadOnlySet<string>? availablePlugins = null) : IPluginContext, IDisposable
{
    readonly CancellationTokenSource lifetime = new();
    readonly List<Task> backgroundTasks = [];

    public IApplication Application { get; } = application;
    public RecordingHostEvents HostEvents { get; } = new();
    public RecordingAnalyzerRegistry AnalyzerRegistry { get; } = new();
    public IPluginHostEvents Events => HostEvents;
    public IPluginAnalyzerRegistry Analyzers => AnalyzerRegistry;
    public bool IsPluginAvailable(string internalName)
        => availablePlugins?.Contains(internalName) == true;

    public void RunBackground(Func<CancellationToken, ValueTask> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        backgroundTasks.Add(Task.Run(() => operation(lifetime.Token).AsTask(), lifetime.Token));
    }

    public void Dispose()
    {
        lifetime.Cancel();
        try
        {
            Task.WhenAll(backgroundTasks).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException)) { }
        HostEvents.Dispose();
        AnalyzerRegistry.Dispose();
        lifetime.Dispose();
    }
}

sealed class TempCurrentDirectory : IDisposable
{
    readonly string originalCurrentDirectory = Directory.GetCurrentDirectory();

    public TempCurrentDirectory(string pluginId)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ura-plugin-runtime-smoke",
            $"{pluginId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
        Directory.SetCurrentDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
        Directory.Delete(Path, recursive: true);
    }
}

static class HistoryConfigDialog
{
    internal static void SetHistoryLimit(Dialog dialog, int value)
    {
        var views = Descendants(dialog).ToArray();
        if (views.OfType<NumericUpDown<int>>().FirstOrDefault() is { } numeric)
        {
            numeric.Value = value;
            return;
        }

        if (views.OfType<TextField>().FirstOrDefault() is { } text)
        {
            text.Text = value.ToString(CultureInfo.InvariantCulture);
            return;
        }

        throw new InvalidOperationException($"Config dialog {dialog.Title} has no historyLimit editor.");
    }

    internal static void AcceptButton(IApplication application, Dialog dialog, string text)
    {
        var button = dialog.Buttons.SingleOrDefault(
            candidate => candidate.Text?.ToString().Contains(text, StringComparison.Ordinal) == true)
            ?? throw new InvalidOperationException($"Config dialog {dialog.Title} has no {text} button.");
        button.SetFocus();
        application.Keyboard.RaiseKeyDownEvent(Key.Enter);
    }

    internal static IEnumerable<View> Descendants(View root)
    {
        yield return root;
        foreach (var child in root.SubViews)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }
}
