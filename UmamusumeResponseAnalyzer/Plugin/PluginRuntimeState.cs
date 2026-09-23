using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace UmamusumeResponseAnalyzer.Plugin;

internal enum PluginLifecyclePhase
{
    Created,
    Loaded,
    Initialized,
    Started,
    ShuttingDown,
    Stopped,
}

internal sealed record PluginRuntimeSnapshot(
    FrozenDictionary<string, PluginManager.PluginMetadata> Metadatas,
    ImmutableArray<string> FailedPlugins,
    ImmutableArray<IPlugin> LoadedPlugins)
{
    internal static PluginRuntimeSnapshot Empty { get; } = new(
        new Dictionary<string, PluginManager.PluginMetadata>(StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
        [],
        []);
}

internal sealed record AnalyzerRuntimeSnapshot(
    ImmutableArray<AnalyzerRegistration> Request,
    ImmutableArray<AnalyzerRegistration> Response)
{
    internal FrozenDictionary<Type, ImmutableArray<AnalyzerRegistration>> RequestByEndpoint { get; } = Request
        .GroupBy(registration => registration.EndpointType)
        .ToFrozenDictionary(group => group.Key, group => group.ToImmutableArray());
    internal FrozenDictionary<Type, ImmutableArray<AnalyzerRegistration>> ResponseByEndpoint { get; } = Response
        .GroupBy(registration => registration.EndpointType)
        .ToFrozenDictionary(group => group.Key, group => group.ToImmutableArray());
    internal static AnalyzerRuntimeSnapshot Empty { get; } = new([], []);
}

internal sealed class PluginRuntimeState
{
    PluginRuntimeSnapshot snapshot = PluginRuntimeSnapshot.Empty;
    AnalyzerRuntimeSnapshot analyzers = AnalyzerRuntimeSnapshot.Empty;
    int shutdownRequested;

    internal Dictionary<string, PluginManager.PluginMetadata> Metadatas { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    internal List<string> FailedPlugins { get; } = [];
    internal List<IPlugin> LoadedPlugins { get; } = [];
    internal List<HashSet<string>> ContextGroups { get; } = [];
    internal Dictionary<string, PluginManager.PluginLoadContext> Contexts { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    internal PluginLifecyclePhase Phase { get; set; }
    internal ConditionalWeakTable<IPlugin, PluginLifecycle> Lifecycles { get; } = new();
    internal SemaphoreSlim LifecycleGate { get; } = new(1, 1);

    internal bool ShutdownRequested
    {
        get => Volatile.Read(ref shutdownRequested) != 0;
        set => Volatile.Write(ref shutdownRequested, value ? 1 : 0);
    }

    internal PluginRuntimeSnapshot ReadSnapshot()
        => Volatile.Read(ref snapshot);

    internal AnalyzerRuntimeSnapshot ReadAnalyzers()
        => Volatile.Read(ref analyzers);

    internal void Publish()
    {
        Volatile.Write(ref snapshot, new(
            Metadatas.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            [.. FailedPlugins],
            [.. LoadedPlugins]));
    }

    internal void PublishAnalyzers(AnalyzerRuntimeSnapshot value)
        => Volatile.Write(ref analyzers, value);
}
