using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Plugin
{
    internal static partial class PluginManager
    {
        internal sealed record PluginRuntimeStatus(
            string InternalName,
            string DisplayName,
            string Author,
            Version? Version,
            bool IsLoaded,
            bool IsAvailable,
            string? Error);

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
                string.Format(i18n.CallbackUnavailable, InternalName(plugin)));
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

            return generationLease;
        }

        internal static IDisposable EnterPluginConfiguration(
            IPlugin plugin,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TryEnterPluginInspection(plugin) ?? throw new InvalidOperationException(
                string.Format(i18n.ConfigurationUnavailable, InternalName(plugin)));
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
                        : new AggregateException(i18n.NotificationDiagnosticsFailed, failure, notificationError),
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

        static InvalidOperationException LifecyclePhaseFailure(
            string operation,
            PluginLifecyclePhase? phase = null)
            => new(string.Format(i18n.LifecyclePhaseInvalid, phase ?? Lifecycle.Phase, operation));

        internal static IReadOnlyList<IPlugin> SnapshotLoadedPlugins()
            => Runtime.ReadSnapshot().LoadedPlugins;

        internal static IPlugin? FindLoadedPlugin(string internalName)
            => Runtime.ReadSnapshot().LoadedPlugins.FirstOrDefault(plugin =>
                string.Equals(InternalName(plugin), internalName, StringComparison.OrdinalIgnoreCase));

        internal static IReadOnlyList<PluginRuntimeStatus> SnapshotPluginStatuses()
        {
            var snapshot = Runtime.ReadSnapshot();
            var loadedByName = snapshot.LoadedPlugins.ToDictionary(InternalName, StringComparer.OrdinalIgnoreCase);
            var names = snapshot.Metadatas.Keys.Concat(loadedByName.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase);
            List<PluginRuntimeStatus> statuses = [];
            foreach (var name in names)
            {
                loadedByName.TryGetValue(name, out var plugin);
                var metadata = snapshot.Metadatas.GetValueOrDefault(name);
                var generation = plugin is not null && PluginGenerations.TryGetValue(plugin, out var current)
                    ? current
                    : null;
                statuses.Add(new(
                    name,
                    metadata?.DisplayName ?? name,
                    metadata?.Author ?? string.Empty,
                    metadata?.Version,
                    generation is { IsClosed: false } && !IsShuttingDown(),
                    metadata is not null,
                    generation?.Failure ?? snapshot.Failures.GetValueOrDefault(name)));
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
                : throw new InvalidOperationException(string.Format(i18n.GenerationMissing, InternalName(plugin)));

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
            if (!Runtime.LifecycleGate.Wait(0))
            {
                if (Runtime.ShutdownRequested)
                    throw LifecyclePhaseFailure("Init", PluginLifecyclePhase.ShuttingDown);
                throw new InvalidOperationException(
                    i18n.InitializationTransactionBusy);
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
                        string.Format(i18n.RuntimeStateNotEmpty, Lifecycle.Phase));

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

        internal static void InitializePlugin(IPlugin plugin)
        {
            var generation = GenerationFor(plugin);
            using var initialization = generation.EnterInitialization();
            try
            {
                using var owner = HotkeyManager.RegisterScope(plugin);
                using var registrations = BeginRegistrationStage(plugin, includeAttributeAnalyzers: true);
                var available = LifecycleMetadatas.TryGetValue(InternalName(plugin), out var metadata)
                    ? metadata.Dependencies.Where(name =>
                        LifecycleMetadatas.TryGetValue(name, out var dependency) &&
                        ShouldLoadPluginForCurrentTargets(dependency)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                plugin.Initialize(new PluginContext(TerminalUi.RequireHost(), plugin, HostEvents, available));
                var plan = registrations.Commit();
                generation.CompleteInitialization();
                CommitAnalyzerRegistrations(plan.Analyzers);
                generation.Open();
                generation.RunBackground(plan.BackgroundOperations);
            }
            catch
            {
                generation.AbortInitialization();
                throw;
            }
        }

        internal static void InitializeLoadedPlugins()
        {
            using var transaction = EnterLifecycleTransaction(nameof(InitializeLoadedPlugins));
            if (Lifecycle.Phase is PluginLifecyclePhase.Initialized or PluginLifecyclePhase.Started)
                return;
            if (Lifecycle.Phase != PluginLifecyclePhase.Loaded)
                throw LifecyclePhaseFailure(nameof(InitializeLoadedPlugins));

            foreach (var plugin in LifecycleLoadedPlugins.ToArray())
            {
                if (GenerationFor(plugin).IsAccepting)
                    continue;
                try
                {
                    InitializePlugin(plugin);
                }
                catch (Exception ex)
                {
                    var name = InternalName(plugin);
                    Exception failure = new InvalidOperationException(string.Format(i18n.InitializationFailed, name), ex);
                    Lifecycle.Failures[name] = ex.GetBaseException().Message;
                    var path = LifecycleMetadatas.GetValueOrDefault(name)?.PackagePath ?? name;
                    if (!LifecycleFailedPlugins.Contains(path))
                        LifecycleFailedPlugins.Add(path);
                    try { CleanupPluginAsync(plugin, flush: true).GetAwaiter().GetResult(); }
                    catch (Exception cleanupEx)
                    {
                        failure = new AggregateException(i18n.InitializationCleanupFailed, failure, cleanupEx);
                    }
                    ReportPluginDiagnostic(failure);
                }
            }
            Lifecycle.Phase = PluginLifecyclePhase.Initialized;
        }

        internal static async Task TriggerStartedAsync(CancellationToken cancellationToken = default)
        {
            await Runtime.LifecycleGate.WaitAsync(cancellationToken);
            using var transaction = new LifecycleTransaction();
            if (Runtime.ShutdownRequested)
                throw LifecyclePhaseFailure(nameof(TriggerStartedAsync));
            if (Lifecycle.Phase == PluginLifecyclePhase.Started)
                return;
            if (Lifecycle.Phase != PluginLifecyclePhase.Initialized)
                throw LifecyclePhaseFailure(nameof(TriggerStartedAsync));

            await HostEvents.TriggerStartedAsync(cancellationToken: cancellationToken);
            Lifecycle.Phase = PluginLifecyclePhase.Started;
        }

        internal static void DisposeHostEventSubscriptions(IPlugin plugin)
            => HostEvents.DisposeFor(plugin);

        internal static void ClearHostEventSubscriptions()
            => HostEvents.Clear();

        internal static async Task ShutdownAsync()
        {
            Runtime.ShutdownRequested = true;
            await Runtime.LifecycleGate.WaitAsync();
            try
            {
                if (Lifecycle.Phase == PluginLifecyclePhase.Stopped)
                    return;

                Lifecycle.Phase = PluginLifecyclePhase.ShuttingDown;
                var plugins = LifecycleLoadedPlugins.AsEnumerable().Reverse().ToArray();
                foreach (var plugin in plugins)
                    _ = GenerationFor(plugin).Close();
                List<Exception> failures = [];
                foreach (var plugin in plugins)
                {
                    try { await CleanupPluginAsync(plugin, flush: true); }
                    catch (Exception ex) { failures.Add(ex); }
                }
                LifecycleLoadedPlugins.Clear();
                LifecycleMetadatas.Clear();
                LifecycleFailedPlugins.Clear();
                Lifecycle.Failures.Clear();
                LifecycleContextGroups.Clear();
                LifecycleContexts.Clear();
                ClearAnalyzerRegistrations();
                ClearHostEventSubscriptions();
                Lifecycle.Phase = PluginLifecyclePhase.Stopped;
                if (failures.Count != 0)
                    throw new AggregateException(i18n.CleanupFailed, failures);
            }
            finally
            {
                Runtime.Publish();
                Runtime.LifecycleGate.Release();
            }
        }

        static IDisposable EnterLifecycleTransaction(string operation)
        {
            if (Runtime.ShutdownRequested)
                throw LifecyclePhaseFailure(operation);
            Runtime.LifecycleGate.Wait();
            if (Runtime.ShutdownRequested)
            {
                Runtime.LifecycleGate.Release();
                throw LifecyclePhaseFailure(operation);
            }
            return new LifecycleTransaction();
        }

        sealed class LifecycleTransaction : IDisposable
        {
            public void Dispose()
            {
                try { Runtime.Publish(); }
                finally { Runtime.LifecycleGate.Release(); }
            }
        }
    }
}
