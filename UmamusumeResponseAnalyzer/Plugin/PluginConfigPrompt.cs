using UmamusumeResponseAnalyzer.TerminalGui;
using i18n = UmamusumeResponseAnalyzer.Localization.Config;

namespace UmamusumeResponseAnalyzer.Plugin
{
    internal static class PluginConfigPrompt
    {
        internal static async Task PromptAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var plugins = BuildPluginChoices(
                    PluginManager.SnapshotActivePluginMetadatas());
                var choices = plugins
                    .Select(x => (Label: x.Key, InternalName: (string?)x.Value))
                    .Append((i18n.Return, null))
                    .ToArray();
                var selected = ModalDialogs.Menu(
                    i18n.Tabs_Plugin_Title,
                    choices,
                    x => x.Label,
                    cancellationToken: cancellationToken);
                if (selected.InternalName is null)
                    return;
                var plugin = PluginManager.FindLoadedPlugin(selected.InternalName)
                    ?? throw new InvalidOperationException(string.Format(i18n.PluginUnloaded, selected.InternalName));
                await RunAsync(plugin, cancellationToken);
            }
        }

        internal static SortedDictionary<string, string> BuildPluginChoices(
            IEnumerable<PluginManager.PluginMetadata> plugins)
        {
            var list = plugins.ToList();
            var duplicateNames = list
                .GroupBy(x => x.DisplayName, StringComparer.Ordinal)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToHashSet(StringComparer.Ordinal);

            var choices = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var plugin in list)
            {
                var baseLabel = duplicateNames.Contains(plugin.DisplayName)
                    ? $"{plugin.DisplayName} ({plugin.Author}/{plugin.PluginName})"
                    : plugin.DisplayName;

                var label = baseLabel;
                for (var suffix = 2; choices.ContainsKey(label); suffix++)
                    label = $"{baseLabel} #{suffix}";

                choices.Add(label, plugin.PluginName);
            }
            return choices;
        }

        public static async Task RunAsync(IPlugin plugin, CancellationToken cancellationToken = default)
        {
            using var lifecycle = PluginManager.EnterPluginConfiguration(plugin, cancellationToken);
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
