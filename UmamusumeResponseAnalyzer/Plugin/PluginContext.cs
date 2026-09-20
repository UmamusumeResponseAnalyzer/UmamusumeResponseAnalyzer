using Terminal.Gui.App;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed class PluginContext(
    UiHost host,
    IPlugin plugin,
    PluginHostEvents events,
    IReadOnlySet<string> availablePlugins) : IPluginContext, IPluginHostEvents
{
    public IApplication Application { get; } = host.Application;
    public IPluginHostEvents Events => this;
    public IPluginAnalyzerRegistry Analyzers { get; } = PluginManager.AnalyzersFor(plugin);

    public bool IsPluginAvailable(string internalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalName);
        return availablePlugins.Contains(internalName);
    }

    public void OnStarted(Func<CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        PluginManager.ValidateRegistrationStage(plugin);
        events.SubscribeStarted(plugin, handler);
    }

    public void RunBackground(Func<CancellationToken, ValueTask> operation)
        => PluginManager.StageBackgroundOperation(plugin, operation);
}

internal sealed class PluginHostEvents
{
    StartedHandler[] startedHandlers = [];

    internal void SubscribeStarted(IPlugin plugin, Func<CancellationToken, ValueTask> handler)
        => Volatile.Write(
            ref startedHandlers,
            [.. Volatile.Read(ref startedHandlers), new(plugin, handler)]);

    internal async Task TriggerStartedAsync(
        IEnumerable<IPlugin>? plugins = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selectedPlugins = plugins?.ToHashSet<IPlugin>(ReferenceEqualityComparer.Instance);
        var selected = Volatile.Read(ref startedHandlers)
            .Where(subscription => selectedPlugins is null || selectedPlugins.Contains(subscription.Plugin))
            .ToArray();

        foreach (var plugin in selected
                     .Select(subscription => subscription.Plugin)
                     .Distinct<IPlugin>(ReferenceEqualityComparer.Instance))
        {
            using var generation = PluginManager.EnterPluginCallback(plugin, cancellationToken);
            foreach (var subscription in selected.Where(candidate => ReferenceEquals(candidate.Plugin, plugin)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var ownerScope = HotkeyManager.RegisterScope(plugin);
                using var stage = PluginManager.BeginRegistrationStage(plugin);
                try
                {
                    await subscription.Handler(cancellationToken);
                    stage.Commit();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var failure = new InvalidOperationException(
                        $"插件事件处理错误: plugin={PluginManager.InternalName(plugin)}, " +
                        PluginManager.DescribeException(ex));
                    _ = PluginManager.ReportPluginFailure("Plugin", failure, ex.ToString());
                }
            }
        }
    }

    internal void DisposeFor(IPlugin plugin)
        => Volatile.Write(
            ref startedHandlers,
            [.. Volatile.Read(ref startedHandlers)
                .Where(subscription => !ReferenceEquals(subscription.Plugin, plugin))]);

    internal void Clear()
        => Volatile.Write(ref startedHandlers, []);

    sealed record StartedHandler(
        IPlugin Plugin,
        Func<CancellationToken, ValueTask> Handler);
}
