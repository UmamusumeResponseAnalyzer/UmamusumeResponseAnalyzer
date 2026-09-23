using Terminal.Gui.App;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed class PluginContext(
    UiHost host,
    IPlugin plugin,
    IReadOnlySet<string> availablePlugins) : IPluginContext, IPluginAnalyzerRegistry
{
    public IApplication Application { get; } = host.Application;
    public IPluginAnalyzerRegistry Analyzers => this;

    public void Register<TPayload>(
        AnalyzerKind kind,
        IReadOnlyList<EndpointPattern> patterns,
        Func<AnalyzerInvocation<TPayload>, ValueTask> handler,
        int priority = 0)
        => PluginManager.StageProgrammaticAnalyzers(plugin, kind, patterns, handler, priority);

    public bool IsPluginAvailable(string internalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalName);
        return availablePlugins.Contains(internalName);
    }

    public void ReportBackgroundFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        PluginManager.ReportBackgroundFailure(plugin, error);
    }
}
