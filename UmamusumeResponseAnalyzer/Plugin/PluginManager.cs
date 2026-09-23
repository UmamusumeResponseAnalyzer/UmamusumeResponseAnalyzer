using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
using System.Collections.Frozen;
using System.Collections.Immutable;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Plugin
{
    internal static partial class PluginManager
    {
        static readonly PluginRuntimeState Runtime = new();

        internal static FrozenDictionary<string, PluginMetadata> Metadatas
            => Runtime.ReadSnapshot().Metadatas;
        internal static IReadOnlyList<string> FailedPlugins
            => Runtime.ReadSnapshot().FailedPlugins;
        internal static IReadOnlyList<IPlugin> LoadedPlugins
            => Runtime.ReadSnapshot().LoadedPlugins;
        internal static IReadOnlyList<ImmutableHashSet<string>> ContextGroups
            => [.. Runtime.ContextGroups.Select(group => group.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase))];
        internal static FrozenDictionary<string, PluginLoadContext> Contexts
            => Runtime.Contexts.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
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
                !Runtime.Lifecycles.TryGetValue(plugin, out var lifecycle) ||
                !lifecycle.TryEnterCallback(out var lifecycleLease))
                return null;

            return lifecycleLease;
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
                !Runtime.Lifecycles.TryGetValue(plugin, out var lifecycle) ||
                !lifecycle.TryEnterInspection(out var inspection))
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
                Runtime.Lifecycles.TryGetValue(plugin, out var lifecycle) &&
                lifecycle.TryEnterRegistration(out var registration))
                return registration!;

            return null;
        }

        static bool IsShuttingDown()
            => Runtime.ShutdownRequested;

        static InvalidOperationException LifecyclePhaseFailure(
            string operation,
            PluginLifecyclePhase? phase = null)
            => new(string.Format(i18n.LifecyclePhaseInvalid, phase ?? Runtime.Phase, operation));

        internal static IReadOnlyList<IPlugin> SnapshotLoadedPlugins()
            => Runtime.ReadSnapshot().LoadedPlugins;

        internal static IPlugin? FindLoadedPlugin(string internalName)
            => Runtime.ReadSnapshot().LoadedPlugins.FirstOrDefault(plugin =>
                string.Equals(InternalName(plugin), internalName, StringComparison.OrdinalIgnoreCase));

        internal static bool IsPluginActive(IPlugin plugin)
            => !Runtime.ShutdownRequested &&
               Runtime.Lifecycles.TryGetValue(plugin, out var lifecycle) && !lifecycle.IsClosed;

        internal static IReadOnlyList<PluginMetadata> SnapshotActivePluginMetadatas()
        {
            var snapshot = Runtime.ReadSnapshot();
            return snapshot.LoadedPlugins.Where(IsPluginActive)
                .Select(plugin => snapshot.Metadatas[InternalName(plugin)]).ToArray();
        }

        internal static string InternalName(IPlugin plugin)
            => plugin.GetType().Assembly.GetName().Name
               ?? plugin.GetType().FullName
               ?? nameof(IPlugin);

        static PluginLifecycle LifecycleFor(IPlugin plugin)
            => Runtime.Lifecycles.GetValue(plugin, static value => new(value));

        static PluginLifecycle RequireLifecycle(IPlugin plugin)
            => Runtime.Lifecycles.TryGetValue(plugin, out var lifecycle)
                ? lifecycle
                : throw new InvalidOperationException(string.Format(i18n.LifecycleMissing, InternalName(plugin)));

        internal static void Init(CancellationToken cancellationToken = default)
        {
            using var transaction = EnterInitializationTransaction(cancellationToken);
            Directory.CreateDirectory("Plugins");
            LoadMetadatas();
            BuildGroups();
            LoadPlugins();
            Runtime.Phase = PluginLifecyclePhase.Loaded;
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
                if (Runtime.Phase is not PluginLifecyclePhase.Created and
                    not PluginLifecyclePhase.Stopped)
                    throw LifecyclePhaseFailure("Init");
                if (Runtime.Metadatas.Count != 0 || Runtime.FailedPlugins.Count != 0 ||
                    Runtime.LoadedPlugins.Count != 0 || Runtime.ContextGroups.Count != 0 ||
                    Runtime.Contexts.Count != 0 || Runtime.ReadAnalyzers().Request.Length != 0 ||
                    Runtime.ReadAnalyzers().Response.Length != 0)
                    throw new InvalidOperationException(
                        string.Format(i18n.RuntimeStateNotEmpty, Runtime.Phase));

                Runtime.ShutdownRequested = false;
                Runtime.Phase = PluginLifecyclePhase.Created;
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
            var lifecycle = LifecycleFor(plugin);
            using var initialization = lifecycle.EnterInitialization();
            try
            {
                using var owner = HotkeyManager.RegisterScope(plugin);
                using var registrations = BeginRegistrationStage(plugin, includeAttributeAnalyzers: true);
                var available = Runtime.Metadatas.TryGetValue(InternalName(plugin), out var metadata)
                    ? metadata.Dependencies.Where(name =>
                         Runtime.Metadatas.ContainsKey(name)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                plugin.Initialize(new PluginContext(TerminalUi.RequireHost(), plugin, available));
                var plan = registrations.Commit();
                lifecycle.CompleteInitialization();
                CommitAnalyzerRegistrations(plan);
                lifecycle.Open();
            }
            catch
            {
                lifecycle.AbortInitialization();
                throw;
            }
        }

        internal static void InitializeLoadedPlugins()
        {
            using var transaction = EnterLifecycleTransaction(nameof(InitializeLoadedPlugins));
            if (Runtime.Phase is PluginLifecyclePhase.Initialized or PluginLifecyclePhase.Started)
                return;
            if (Runtime.Phase != PluginLifecyclePhase.Loaded)
                throw LifecyclePhaseFailure(nameof(InitializeLoadedPlugins));

            foreach (var plugin in Runtime.LoadedPlugins.ToArray())
            {
                if (LifecycleFor(plugin).IsAccepting)
                    continue;
                try
                {
                    InitializePlugin(plugin);
                }
                catch (Exception ex)
                {
                    var name = InternalName(plugin);
                    Exception failure = new InvalidOperationException(string.Format(i18n.InitializationFailed, name), ex);
                    var reportedFailure = LifecycleFor(plugin).Failure;
                    var path = Runtime.Metadatas.GetValueOrDefault(name)?.PackagePath ?? name;
                    if (!Runtime.FailedPlugins.Contains(path))
                        Runtime.FailedPlugins.Add(path);
                    var cleanupFailed = false;
                    try { CleanupPluginAsync(plugin, flush: true).GetAwaiter().GetResult(); }
                    catch (Exception cleanupEx)
                    {
                        failure = new AggregateException(i18n.InitializationCleanupFailed, failure, cleanupEx);
                        cleanupFailed = true;
                    }
                    if (reportedFailure is null || cleanupFailed)
                        ReportPluginDiagnostic(failure);
                }
            }
            Runtime.Phase = PluginLifecyclePhase.Initialized;
        }

        internal static async Task TriggerStartedAsync(CancellationToken cancellationToken = default)
        {
            await Runtime.LifecycleGate.WaitAsync(cancellationToken);
            using var transaction = new LifecycleTransaction();
            if (Runtime.ShutdownRequested)
                throw LifecyclePhaseFailure(nameof(TriggerStartedAsync));
            if (Runtime.Phase == PluginLifecyclePhase.Started)
                return;
            if (Runtime.Phase != PluginLifecyclePhase.Initialized)
                throw LifecyclePhaseFailure(nameof(TriggerStartedAsync));

            foreach (var plugin in Runtime.LoadedPlugins.ToArray())
                await StartPluginAsync(plugin, cancellationToken);
            Runtime.Phase = PluginLifecyclePhase.Started;
        }

        internal static async Task StartPluginAsync(IPlugin plugin, CancellationToken cancellationToken = default)
        {
            using var callback = TryEnterPluginCallback(plugin, cancellationToken);
            if (callback is null)
                return;

            using var owner = HotkeyManager.RegisterScope(plugin);
            using var stage = BeginRegistrationStage(plugin);
            try
            {
                await plugin.StartAsync(cancellationToken);
                CommitRegistrationStage(plugin, stage.Commit());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (IsPluginFaulted(plugin) || TryDisableForMissingAssembly(plugin, ex, "StartAsync"))
                    return;

                var failure = new InvalidOperationException(
                    string.Format(i18n.StartFailed, InternalName(plugin), DescribeException(ex)));
                ReportPluginFailure("Plugin", failure, ex.ToString());
            }
        }

        internal static async Task ShutdownAsync()
        {
            Runtime.ShutdownRequested = true;
            await Runtime.LifecycleGate.WaitAsync();
            try
            {
                if (Runtime.Phase == PluginLifecyclePhase.Stopped)
                    return;

                Runtime.Phase = PluginLifecyclePhase.ShuttingDown;
                var plugins = Runtime.LoadedPlugins.AsEnumerable().Reverse().ToArray();
                foreach (var plugin in plugins)
                    _ = LifecycleFor(plugin).Close();
                List<Exception> failures = [];
                foreach (var plugin in plugins)
                {
                    try { await CleanupPluginAsync(plugin, flush: true); }
                    catch (Exception ex) { failures.Add(ex); }
                }
                Runtime.LoadedPlugins.Clear();
                Runtime.Metadatas.Clear();
                Runtime.FailedPlugins.Clear();
                Runtime.ContextGroups.Clear();
                Runtime.Contexts.Clear();
                ClearAnalyzerRegistrations();
                Runtime.Phase = PluginLifecyclePhase.Stopped;
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
