using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Plugin
{
    internal static class PluginConfigPrompt
    {
        public static async Task RunAsync(IPlugin plugin, CancellationToken cancellationToken = default)
        {
            using var generation = PluginManager.EnterPluginConfiguration(plugin, cancellationToken);
            using var owner = HotkeyManager.RegisterScope(plugin);

            try
            {
                await plugin.ConfigPromptAsync(TerminalUi.RequireHost().Application, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (Exception ex) when (PluginManager.TryDisableForMissingAssembly(plugin, ex, "ConfigPromptAsync")) { }
        }
    }
}
