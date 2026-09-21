using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Terminal.Gui.App;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Plugin
{
    internal static partial class PluginManager
    {
        internal enum PluginLifecycleOutcome
        {
            Failed,
            Succeeded,
        }

        internal sealed record PluginLifecycleResult(
            string PluginName,
            PluginLifecycleOutcome Outcome);

        internal sealed record PluginRuntimeStatus(
            string InternalName,
            string DisplayName,
            string Author,
            Version? Version,
            bool IsLoaded,
            bool IsAvailable);

        static readonly PluginRuntimeState Runtime = new();
        static PluginRuntimeMutableState Lifecycle => Runtime.Mutable;
        static Dictionary<string, PluginMetadata> LifecycleMetadatas => Lifecycle.Metadatas;
        static List<string> LifecycleFailedPlugins => Lifecycle.FailedPlugins;
        static List<IPlugin> LifecycleLoadedPlugins => Lifecycle.LoadedPlugins;
        static List<HashSet<string>> LifecycleContextGroups => Lifecycle.ContextGroups;
        static Dictionary<string, PluginLoadContext> LifecycleContexts => Lifecycle.Contexts;

        internal static FrozenDictionary<string, PluginMetadata> Metadatas
            => Runtime.ReadSnapshot().Metadatas;
        internal static IReadOnlyList<string> FailedPlugins
            => Runtime.ReadSnapshot().FailedPlugins;
        internal static IReadOnlyList<IPlugin> LoadedPlugins
            => Runtime.ReadSnapshot().LoadedPlugins;
        internal static IReadOnlyList<ImmutableHashSet<string>> ContextGroups
            => [.. LifecycleContextGroups.Select(group => group.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase))];
        internal static FrozenDictionary<string, PluginLoadContext> Contexts
            => LifecycleContexts.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        internal static IReadOnlyList<AnalyzerRegistration> RequestAnalyzerMethods
            => Runtime.ReadAnalyzers().Request;
        internal static IReadOnlyList<AnalyzerRegistration> ResponseAnalyzerMethods
            => Runtime.ReadAnalyzers().Response;
        static readonly string HostAssemblyName = typeof(PluginManager).Assembly.GetName().Name ?? "UmamusumeResponseAnalyzer";
        static readonly FrozenSet<string> SharedAssemblyNames = new[]
        {
            HostAssemblyName,
            "Terminal.Gui",
            "Watson.Lite",
            "WatsonWebserver.Core",
            "WatsonWebserver.Lite",
        }.ToFrozenSet(StringComparer.Ordinal);
        static PluginHostEvents HostEvents => Runtime.HostEvents;
        static ConditionalWeakTable<IPlugin, PluginGeneration> PluginGenerations => Runtime.Generations;

        internal static IDisposable EnterPluginCallback(
            IPlugin plugin,
            CancellationToken cancellationToken = default)
        {
            var callback = TryEnterPluginCallback(plugin, cancellationToken);
            return callback ?? throw new InvalidOperationException(
                $"插件已卸载或 generation 已关闭，拒绝启动回调: {InternalName(plugin)}");
        }

        internal static IDisposable? TryEnterPluginCallback(
            IPlugin plugin,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsShuttingDown() ||
                !PluginGenerations.TryGetValue(plugin, out var generation) ||
                !generation.TryEnterCallback(out var generationLease))
                return null;

            return generation.EnterCallbackFlow(generationLease!);
        }

        internal static IDisposable EnterPluginConfiguration(
            IPlugin plugin,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TryEnterPluginInspection(plugin) ?? throw new InvalidOperationException(
                $"插件已卸载或 generation 已关闭，拒绝打开设置: {InternalName(plugin)}");
        }

        static IDisposable? TryEnterPluginInspection(IPlugin plugin)
        {
            if (IsShuttingDown() ||
                !PluginGenerations.TryGetValue(plugin, out var generation) ||
                !generation.TryEnterInspection(out var inspection))
                return null;

            return inspection;
        }

        internal static string DescribeException(Exception exception)
        {
            var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
            return $"exception={exceptionType}, message={exception.Message}";
        }

        internal static void ReportPluginFailure(
            string source,
            InvalidOperationException failure,
            string details)
        {
            Exception? notificationError = null;
            try
            {
                TerminalUi.Notify("Plugin", failure.Message, UiSeverity.Error);
            }
            catch (Exception ex)
            {
                notificationError = ex;
            }

            try
            {
                TerminalUi.LogException(
                    source,
                    notificationError is null
                        ? failure
                        : new AggregateException("插件错误及 notification diagnostics 失败。", failure, notificationError),
                    details: details);
            }
            catch
            {
            }
        }

        static void ReportPluginDiagnostic(Exception exception)
        {
            TerminalUi.LogException("Plugin", exception);
            TerminalUi.Notify(
                "Plugin",
                TerminalUi.FormatExceptionLogMessage(exception),
                UiSeverity.Error);
        }

        static void ReportPluginDiagnostic(string message, UiSeverity severity)
        {
            TerminalUi.Log("Plugin", message, severity);
            TerminalUi.Notify("Plugin", message, severity);
        }

        internal static IDisposable? TryEnterPluginRegistration(IPlugin plugin)
        {
            if (!IsShuttingDown() &&
                PluginGenerations.TryGetValue(plugin, out var generation) &&
                generation.TryEnterRegistration(out var registration))
                return registration!;

            return null;
        }

        static bool IsShuttingDown()
            => Runtime.ShutdownRequested;

        static void RejectCallbackLifecycleReentry()
        {
            if (PluginGeneration.HasActiveCallbackFlow)
                throw new InvalidOperationException(
                    "插件 callback 内禁止启动 lifecycle 操作；请在 callback 返回后再调用。");
        }

        static InvalidOperationException LifecyclePhaseFailure(
            string operation,
            PluginLifecyclePhase? phase = null)
            => new($"当前 phase={phase ?? Lifecycle.Phase}，不允许执行插件 {operation}。");

        static void RequireOperationalLifecyclePhase()
        {
            if (Lifecycle.Phase is PluginLifecyclePhase.Loaded or
                PluginLifecyclePhase.Initialized or PluginLifecyclePhase.Started)
                return;

            throw LifecyclePhaseFailure("load/reload/unload");
        }

        internal static IReadOnlyList<IPlugin> SnapshotLoadedPlugins()
            => Runtime.ReadSnapshot().LoadedPlugins;

        internal static IPlugin? FindLoadedPlugin(string internalName)
            => Runtime.ReadSnapshot().LoadedPlugins.FirstOrDefault(plugin =>
                string.Equals(InternalName(plugin), internalName, StringComparison.OrdinalIgnoreCase));

        internal static IReadOnlyList<PluginRuntimeStatus> SnapshotPluginStatuses()
            => BuildPluginStatuses(new Dictionary<string, PluginMetadata>());

        internal static IReadOnlyList<PluginRuntimeStatus> InspectPluginStatuses()
            => BuildPluginStatuses(ScanPluginMetadata(reportFailures: false));

        static IReadOnlyList<PluginRuntimeStatus> BuildPluginStatuses(
            IReadOnlyDictionary<string, PluginMetadata> scanned)
        {
            var snapshot = Runtime.ReadSnapshot();
            var loaded = snapshot.LoadedPlugins;
            var knownByName = snapshot.Metadatas;

            var loadedByName = loaded.ToDictionary(InternalName, StringComparer.OrdinalIgnoreCase);
            var names = scanned.Keys
                .Concat(knownByName.Keys)
                .Concat(loadedByName.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
            List<PluginRuntimeStatus> statuses = [];
            foreach (var name in names)
            {
                loadedByName.TryGetValue(name, out var plugin);
                var metadata = scanned.GetValueOrDefault(name) ?? knownByName.GetValueOrDefault(name);
                var displayName = metadata?.DisplayName ?? name;
                var author = metadata?.Author ?? string.Empty;
                var version = metadata?.Version;
                var isLoaded = plugin is not null && !IsShuttingDown() &&
                               PluginGenerations.TryGetValue(plugin, out var generation) && !generation.IsClosed;

                statuses.Add(new(
                    metadata?.PluginName ?? name,
                    displayName,
                    author,
                    version,
                    isLoaded,
                    metadata is not null));
            }

            return statuses;
        }

        internal static string InternalName(IPlugin plugin)
            => plugin.GetType().Assembly.GetName().Name
               ?? plugin.GetType().FullName
               ?? nameof(IPlugin);

        static PluginGeneration GenerationFor(IPlugin plugin)
            => PluginGenerations.GetValue(plugin, static value => new(value));

        static PluginGeneration RequireGeneration(IPlugin plugin)
            => PluginGenerations.TryGetValue(plugin, out var generation)
                ? generation
                : throw new InvalidOperationException($"插件缺少 runtime generation: {InternalName(plugin)}");

        internal static void Init(CancellationToken cancellationToken = default)
        {
            using var transaction = EnterInitializationTransaction(cancellationToken);
            Directory.CreateDirectory("Plugins");
            LoadMetadatas();
            BuildGroups();
            LoadPlugins();
            Lifecycle.Phase = PluginLifecyclePhase.Loaded;
        }

        static IDisposable EnterInitializationTransaction(CancellationToken cancellationToken)
        {
            RejectCallbackLifecycleReentry();
            if (!Runtime.LifecycleGate.Wait(0))
            {
                if (Runtime.ShutdownRequested)
                    throw LifecyclePhaseFailure("Init", PluginLifecyclePhase.ShuttingDown);
                throw new InvalidOperationException(
                    "已有插件 lifecycle 事务正在运行，无法重新初始化。");
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Lifecycle.Phase is not PluginLifecyclePhase.Created and
                    not PluginLifecyclePhase.Stopped)
                    throw LifecyclePhaseFailure("Init");
                if (LifecycleMetadatas.Count != 0 || LifecycleFailedPlugins.Count != 0 ||
                    LifecycleLoadedPlugins.Count != 0 || LifecycleContextGroups.Count != 0 ||
                    LifecycleContexts.Count != 0 || Runtime.ReadAnalyzers().Request.Length != 0 ||
                    Runtime.ReadAnalyzers().Response.Length != 0)
                    throw new InvalidOperationException(
                        $"当前 phase={Lifecycle.Phase}，插件 runtime state 非空，无法执行 Init。");

                Runtime.ShutdownRequested = false;
                Lifecycle.Phase = PluginLifecyclePhase.Created;
                return new LifecycleTransaction();
            }
            catch
            {
                Runtime.LifecycleGate.Release();
                throw;
            }
        }

        /// <summary>
        /// 调用插件 Initialize，并把期间注册的快捷键归属到该插件实例。
        /// 初始批量加载（Program.cs）与热重载都经此入口，保证 owner 标记一致。
        /// </summary>
        internal static void InitializePlugin(IPlugin plugin)
            => InitializePlugin(plugin, activateCallbacks: true, availableGroupMembers: null);

        static void InitializePlugin(
            IPlugin plugin,
            bool activateCallbacks,
            IReadOnlySet<string>? availableGroupMembers)
        {
            var generation = GenerationFor(plugin);
            using var initialization = generation.EnterInitialization();
            try
            {
                using var owner = HotkeyManager.RegisterScope(plugin);
                using var registrations = BeginRegistrationStage(plugin, includeAttributeAnalyzers: true);
                plugin.Initialize(new PluginContext(
                    TerminalUi.RequireHost(),
                    plugin,
                    HostEvents,
                    DeclaredAvailablePlugins(plugin, availableGroupMembers)));
                registrations.Commit();
                generation.CompleteInitialization();
                if (activateCallbacks)
                    CommitPendingRegistrations([plugin]);
            }
            catch
            {
                generation.AbortInitialization();
                throw;
            }
        }

        internal static void InitializeLoadedPlugins()
        {
            using var transaction = EnterReloadTransaction(nameof(InitializeLoadedPlugins));
            if (Lifecycle.Phase is PluginLifecyclePhase.Initialized or PluginLifecyclePhase.Started)
                return;
            if (Lifecycle.Phase != PluginLifecyclePhase.Loaded)
                throw LifecyclePhaseFailure(nameof(InitializeLoadedPlugins));

            var loaded = LifecycleLoadedPlugins.ToArray();
            var groups = LifecycleContextGroups
                .Select(group => group.ToHashSet(StringComparer.OrdinalIgnoreCase))
                .ToList();

            var grouped = new HashSet<IPlugin>(ReferenceEqualityComparer.Instance);
            foreach (var group in groups)
            {
                var plugins = loaded
                    .Where(plugin => group.Contains(InternalName(plugin)))
                    .ToList();
                if (plugins.Count == 0)
                    continue;
                grouped.UnionWith(plugins);
                var availableGroupMembers = plugins
                    .Select(InternalName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var initialized = true;
                foreach (var plugin in plugins)
                    if (!GenerationFor(plugin).IsAccepting &&
                        !TryInitializePlugin(plugin, committed: false, availableGroupMembers))
                    {
                        initialized = false;
                        break;
                    }

                if (initialized)
                {
                    CommitPendingRegistrations(plugins);
                    continue;
                }

                if (!LifecycleContexts.ContainsKey(GroupKey(group)))
                    throw new InvalidOperationException($"初始化失败的插件组缺少 context: {string.Join("、", group)}");
                var unload = PrepareUnloadGroup(group);
                CompletePendingUnloadsAsync([unload], clearAll: false).GetAwaiter().GetResult();
            }

            foreach (var plugin in loaded.Where(plugin => !grouped.Contains(plugin)))
                if (!GenerationFor(plugin).IsAccepting)
                    TryInitializePlugin(plugin, committed: true);

            Lifecycle.Phase = PluginLifecyclePhase.Initialized;
        }

        static bool TryInitializePlugin(
            IPlugin plugin,
            bool committed,
            IReadOnlySet<string>? availableGroupMembers = null)
            => TryInitializePlugin(plugin, committed, out _, availableGroupMembers);

        static bool TryInitializePlugin(
            IPlugin plugin,
            bool committed,
            out Exception? failure,
            IReadOnlySet<string>? availableGroupMembers = null)
        {
            try
            {
                InitializePlugin(plugin, activateCallbacks: committed, availableGroupMembers);
                failure = null;
                return true;
            }
            catch (Exception ex)
            {
                var internalName = InternalName(plugin);
                var failedPlugin = LifecycleMetadatas.TryGetValue(internalName, out var metadata)
                    ? metadata.FilePath
                    : internalName;
                failure = new InvalidOperationException($"插件 {internalName} 初始化失败", ex);
                if (!LifecycleFailedPlugins.Contains(failedPlugin))
                    LifecycleFailedPlugins.Add(failedPlugin);
                if (committed)
                {
                    try { CompleteFailedPluginLoadAsync(plugin, flush: true).GetAwaiter().GetResult(); }
                    catch (Exception cleanupEx)
                    {
                        failure = new AggregateException(
                            "插件初始化及清理失败。",
                            failure,
                            cleanupEx);
                    }
                }
                ReportPluginDiagnostic(failure);
                return false;
            }
        }

        static IReadOnlySet<string> DeclaredAvailablePlugins(
            IPlugin plugin,
            IReadOnlySet<string>? availableGroupMembers)
        {
            if (availableGroupMembers is null ||
                !LifecycleMetadatas.TryGetValue(InternalName(plugin), out var metadata))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return metadata.Dependencies
                .Where(availableGroupMembers.Contains)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        internal static async Task TriggerStartedAsync(CancellationToken cancellationToken = default)
        {
            using var transaction = EnterReloadTransaction(nameof(TriggerStartedAsync));
            if (Lifecycle.Phase == PluginLifecyclePhase.Started)
                return;
            if (Lifecycle.Phase != PluginLifecyclePhase.Initialized)
                throw LifecyclePhaseFailure(nameof(TriggerStartedAsync));

            await HostEvents.TriggerStartedAsync(cancellationToken: cancellationToken);
            Lifecycle.Phase = PluginLifecyclePhase.Started;
        }

        internal static Task TriggerStartedForPluginsAsync(IEnumerable<IPlugin> plugins, CancellationToken cancellationToken = default)
            => HostEvents.TriggerStartedAsync(plugins, cancellationToken);

        internal static void DisposeHostEventSubscriptions(IPlugin plugin)
            => HostEvents.DisposeFor(plugin);

        internal static void ClearHostEventSubscriptions()
            => HostEvents.Clear();

        internal static async Task ShutdownAsync()
        {
            RejectCallbackLifecycleReentry();
            Runtime.ShutdownRequested = true;
            await Runtime.LifecycleGate.WaitAsync();
            try
            {
                if (Lifecycle.Phase == PluginLifecyclePhase.Stopped)
                    return;

                Lifecycle.Phase = PluginLifecyclePhase.ShuttingDown;
                List<PendingPluginUnload> unloads = [];
                var generations = LifecycleLoadedPlugins.Select(GenerationFor).ToList();
                foreach (var context in LifecycleContexts.Values.Distinct())
                {
                    var contextGenerations = OrderGenerationsForUnload(generations
                        .Where(generation => ReferenceEquals(
                            AssemblyLoadContext.GetLoadContext(generation.Plugin.GetType().Assembly),
                            context))
                        .ToList());
                    unloads.Add(new(
                        contextGenerations.Select(generation => InternalName(generation.Plugin))
                            .ToHashSet(StringComparer.OrdinalIgnoreCase),
                        LifecycleContexts.First(pair => ReferenceEquals(pair.Value, context)).Key,
                        context,
                        contextGenerations,
                        true));
                }

                var contextPlugins = unloads.SelectMany(unload => unload.Generations).ToHashSet();
                var hostGenerations = generations
                    .Where(generation => !contextPlugins.Contains(generation))
                    .Reverse()
                    .ToList();
                if (hostGenerations.Count != 0)
                    unloads.Add(new(
                        hostGenerations.Select(generation => InternalName(generation.Plugin))
                            .ToHashSet(StringComparer.OrdinalIgnoreCase),
                        null,
                        null,
                        hostGenerations,
                        true));

                try
                {
                    await CompletePendingUnloadsAsync(unloads, clearAll: true);
                }
                finally
                {
                    unloads.Clear();
                    Lifecycle.Phase = PluginLifecyclePhase.Stopped;
                }
            }
            finally
            {
                Runtime.Publish();
                Runtime.LifecycleGate.Release();
            }
        }

        static List<PluginGeneration> OrderGenerationsForUnload(
            IReadOnlyCollection<PluginGeneration> generations)
        {
            var byName = generations.ToDictionary(
                generation => InternalName(generation.Plugin),
                StringComparer.OrdinalIgnoreCase);
            return TopologicalOrder(byName.Keys)
                .Reverse()
                .Select(name => byName[name])
                .ToList();
        }

    }
}
