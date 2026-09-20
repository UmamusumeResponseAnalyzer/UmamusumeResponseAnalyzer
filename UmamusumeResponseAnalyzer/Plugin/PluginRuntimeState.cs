using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
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
    ImmutableArray<IPlugin> LoadedPlugins,
    ImmutableArray<ImmutableHashSet<string>> ContextGroups,
    FrozenDictionary<string, PluginManager.PluginLoadContext> Contexts,
    PluginLifecyclePhase Phase)
{
    internal static PluginRuntimeSnapshot Empty { get; } = new(
        new Dictionary<string, PluginManager.PluginMetadata>(StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
        [],
        [],
        [],
        new Dictionary<string, PluginManager.PluginLoadContext>(StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
        PluginLifecyclePhase.Created);
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

internal sealed class PluginRuntimeMutableState
{
    internal Dictionary<string, PluginManager.PluginMetadata> Metadatas { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    internal List<string> FailedPlugins { get; } = [];
    internal List<IPlugin> LoadedPlugins { get; } = [];
    internal List<HashSet<string>> ContextGroups { get; } = [];
    internal Dictionary<string, PluginManager.PluginLoadContext> Contexts { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    internal PluginLifecyclePhase Phase { get; set; }
}

internal sealed class PluginRuntimeState
{
    PluginRuntimeSnapshot snapshot = PluginRuntimeSnapshot.Empty;
    AnalyzerRuntimeSnapshot analyzers = AnalyzerRuntimeSnapshot.Empty;
    int shutdownRequested;

    internal PluginRuntimeMutableState Mutable { get; } = new();
    internal PluginHostEvents HostEvents { get; } = new();
    internal ConditionalWeakTable<IPlugin, PluginGeneration> Generations { get; } = new();
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
        var state = Mutable;
        Volatile.Write(ref snapshot, new(
            state.Metadatas.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            [.. state.FailedPlugins],
            [.. state.LoadedPlugins],
            [.. state.ContextGroups.Select(group => group.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase))],
            state.Contexts.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            state.Phase));
    }

    internal void PublishAnalyzers(AnalyzerRuntimeSnapshot value)
        => Volatile.Write(ref analyzers, value);
}
