namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed class PendingPluginUnload(
    HashSet<string> names,
    string? key,
    PluginManager.PluginLoadContext? context,
    List<PluginGeneration> generations,
    bool removeGroupState)
{
    internal HashSet<string> Names { get; } = names;
    internal string? Key { get; } = key;
    internal PluginManager.PluginLoadContext? Context { get; private set; } = context;
    internal List<PluginGeneration> Generations { get; } = generations;
    internal bool RemoveGroupState { get; } = removeGroupState;

    internal void Release()
    {
        Generations.Clear();
        Context = null;
    }
}

internal sealed record StagedGroupLoad(
    HashSet<string> Names,
    string Key,
    PluginManager.PluginLoadContext Context,
    List<IPlugin> Plugins);
