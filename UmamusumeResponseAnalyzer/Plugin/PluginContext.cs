using Spectre.Console.Rendering;
using UmamusumeResponseAnalyzer.LiveDisplay;

namespace UmamusumeResponseAnalyzer.Plugin
{
    internal sealed class PluginContext(
        IPlugin plugin,
        ILiveDisplayOutput liveDisplay,
        PluginHostEvents events) : IPluginContext
    {
        public ILiveDisplayOutput LiveDisplay { get; } = liveDisplay;
        public IPluginHostEvents Events { get; } = events.ForPlugin(plugin);
    }

    internal sealed class PluginHostEvents
    {
        readonly object gate = new();
        readonly Dictionary<IPlugin, PluginEventOwner> owners = new(ReferenceEqualityComparer.Instance);

        public IPluginHostEvents ForPlugin(IPlugin plugin)
        {
            lock (gate)
            {
                if (!owners.TryGetValue(plugin, out var owner))
                {
                    owner = new(plugin);
                    owners[plugin] = owner;
                }
                return new PluginScopedHostEvents(this, owner);
            }
        }

        IDisposable SubscribeStarted(PluginEventOwner owner, Func<CancellationToken, ValueTask> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            var subscription = new StartedSubscription(this, owner, handler);
            lock (gate)
            {
                if (owner.IsClosed || !owners.TryGetValue(owner.Plugin, out var current) || !ReferenceEquals(current, owner))
                    throw new InvalidOperationException($"插件事件订阅已关闭: plugin={owner.Plugin.Name}");

                owner.Subscriptions.Add(subscription);
            }
            return subscription;
        }

        internal async Task TriggerStartedAsync(IEnumerable<IPlugin>? plugins = null, CancellationToken cancellationToken = default)
        {
            var subscriptions = Snapshot(plugins);
            foreach (var subscription in subscriptions)
                await subscription.InvokeAsync(cancellationToken);
        }

        internal void DisposeFor(IPlugin plugin)
            => DisposeForLater(plugin)();

        internal Action DisposeForLater(IPlugin plugin)
        {
            List<StartedSubscription> subscriptions = [];
            lock (gate)
            {
                if (owners.Remove(plugin, out var owner))
                {
                    owner.MarkClosed();
                    subscriptions = [.. owner.Subscriptions];
                    owner.Subscriptions.Clear();
                }
            }

            foreach (var subscription in subscriptions)
                subscription.MarkDisposed();

            return () =>
            {
                foreach (var subscription in subscriptions)
                    subscription.WaitForIdle();
            };
        }

        internal void Clear()
        {
            List<StartedSubscription> subscriptions;
            lock (gate)
            {
                subscriptions = owners.Values.SelectMany(x => x.Subscriptions).ToList();
                foreach (var owner in owners.Values)
                    owner.MarkClosed();
                owners.Clear();
            }

            foreach (var subscription in subscriptions)
                subscription.MarkDisposed();

            foreach (var subscription in subscriptions)
                subscription.WaitForIdle();
        }

        List<StartedSubscription> Snapshot(IEnumerable<IPlugin>? plugins)
        {
            lock (gate)
            {
                if (plugins is null)
                    return owners.Values.SelectMany(x => x.Subscriptions).Where(x => !x.IsDisposed).ToList();

                var set = plugins.ToHashSet<IPlugin>(ReferenceEqualityComparer.Instance);
                return set
                    .SelectMany(plugin => owners.TryGetValue(plugin, out var owner) ? owner.Subscriptions : [])
                    .Where(x => !x.IsDisposed)
                    .ToList();
            }
        }

        void Remove(StartedSubscription subscription)
        {
            lock (gate)
            {
                if (!owners.TryGetValue(subscription.Plugin, out var owner))
                    return;

                owner.Subscriptions.Remove(subscription);
                if (owner.Subscriptions.Count == 0 && owner.IsClosed)
                    owners.Remove(subscription.Plugin);
            }
        }

        sealed class PluginScopedHostEvents(PluginHostEvents source, PluginEventOwner owner) : IPluginHostEvents
        {
            public IDisposable OnStarted(Func<CancellationToken, ValueTask> handler)
                => source.SubscribeStarted(owner, handler);
        }

        sealed class PluginEventOwner(IPlugin plugin)
        {
            int closed;

            public IPlugin Plugin { get; } = plugin;
            public List<StartedSubscription> Subscriptions { get; } = [];
            public bool IsClosed => Volatile.Read(ref closed) != 0;

            public void MarkClosed()
            {
                Interlocked.Exchange(ref closed, 1);
            }
        }

        sealed class StartedSubscription(
            PluginHostEvents owner,
            PluginEventOwner eventOwner,
            Func<CancellationToken, ValueTask> handler) : IDisposable
        {
            int disposed;
            int inFlight;
            readonly ManualResetEventSlim idle = new(initialState: true);

            public IPlugin Plugin => eventOwner.Plugin;
            public bool IsDisposed => Volatile.Read(ref disposed) != 0;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                    return;

                owner.Remove(this);
            }

            public void MarkDisposed()
            {
                Interlocked.Exchange(ref disposed, 1);
            }

            public void WaitForIdle()
            {
                idle.Wait();
            }

            public async ValueTask InvokeAsync(CancellationToken cancellationToken)
            {
                if (!TryEnter())
                    return;

                try
                {
                    using var callback = PluginManager.EnterPluginCallbackScope();
                    using var scope = KeyboardManager.RegisterScope(Plugin);
                    await handler(cancellationToken);
                }
                catch (Exception ex)
                {
                    LiveDisplayConsole.Notify("Plugin", $"插件事件处理错误: {ex.Message}", LiveDisplaySeverity.Error);
                    LiveDisplayConsole.Log("Plugin", ex.ToString(), LiveDisplaySeverity.Error);
                }
                finally
                {
                    Exit();
                }
            }

            bool TryEnter()
            {
                if (IsDisposed || eventOwner.IsClosed)
                    return false;

                if (Interlocked.Increment(ref inFlight) == 1)
                    idle.Reset();

                if (!IsDisposed && !eventOwner.IsClosed)
                    return true;

                Exit();
                return false;
            }

            void Exit()
            {
                if (Interlocked.Decrement(ref inFlight) == 0)
                    idle.Set();
            }
        }
    }

    internal sealed class NullLiveDisplayOutput : ILiveDisplayOutput
    {
        public static readonly NullLiveDisplayOutput Instance = new();

        NullLiveDisplayOutput()
        {
        }

        public LiveDisplayWorkspace CreateWorkspace(string id, string title)
            => LiveDisplayWorkspace.Create(id, title);

        public void SwitchWorkspace(LiveDisplayWorkspace workspace)
        {
        }

        public void BindWorkspaceHotkey(LiveDisplayWorkspace workspace, ConsoleKey key, ConsoleModifiers modifiers = 0, string? description = null)
        {
        }

        public void SetPanel(LiveDisplayWorkspace workspace, string key, string title, IRenderable content, bool fullBleed = false)
        {
        }

        public void Log(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info)
        {
        }

        public void MarkupLog(LiveDisplayWorkspace workspace, string markup, LiveDisplaySeverity severity = LiveDisplaySeverity.Info)
        {
        }

        public void Notify(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info, TimeSpan? ttl = null)
        {
        }
    }
}
