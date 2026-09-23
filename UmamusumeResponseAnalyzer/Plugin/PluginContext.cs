using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
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
    readonly List<StartedHandler> startedHandlers = [];

    internal void SubscribeStarted(IPlugin plugin, Func<CancellationToken, ValueTask> handler)
        => startedHandlers.Add(new(plugin, handler));

    internal async Task TriggerStartedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = startedHandlers.ToArray();

        foreach (var plugin in selected
                     .Select(subscription => subscription.Plugin)
                     .Distinct<IPlugin>(ReferenceEqualityComparer.Instance))
        {
            using var generation = PluginManager.TryEnterPluginCallback(plugin, cancellationToken);
            if (generation is null)
                continue;

            foreach (var subscription in selected.Where(candidate => ReferenceEquals(candidate.Plugin, plugin)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (PluginManager.IsPluginFaulted(plugin))
                    break;

                using var ownerScope = HotkeyManager.RegisterScope(plugin);
                using var stage = PluginManager.BeginRegistrationStage(plugin);
                try
                {
                    await subscription.Handler(cancellationToken);
                    PluginManager.CommitRegistrationStage(plugin, stage.Commit());
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (PluginManager.TryDisableForMissingAssembly(plugin, ex, "OnStarted"))
                        break;

                    var failure = new InvalidOperationException(
                        string.Format(i18n.EventHandlerFailed, PluginManager.InternalName(plugin),
                            PluginManager.DescribeException(ex)));
                    PluginManager.ReportPluginFailure("Plugin", failure, ex.ToString());
                }
            }
        }
    }

    internal void DisposeFor(IPlugin plugin)
        => startedHandlers.RemoveAll(subscription => ReferenceEquals(subscription.Plugin, plugin));

    internal void Clear()
        => startedHandlers.Clear();

    sealed record StartedHandler(
        IPlugin Plugin,
        Func<CancellationToken, ValueTask> Handler);
}
