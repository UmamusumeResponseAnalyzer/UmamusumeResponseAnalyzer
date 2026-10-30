using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Terminal.Gui.App;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using static UmamusumeResponseAnalyzer.Localization.LaunchMenu;

namespace UmamusumeResponseAnalyzer
{
    public static class UmamusumeResponseAnalyzer
    {
        public static bool Started => Server.IsRunning;
        static readonly string PORTABLE_WORKING_DIRECTORY = Path.Combine(AppContext.BaseDirectory, ".portable");
        public readonly static string WORKING_DIRECTORY = Directory.Exists(PORTABLE_WORKING_DIRECTORY) ? PORTABLE_WORKING_DIRECTORY : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer");
        public static void Main(string[] args)
        {
            var culture = CultureInfo.GetCultureInfo(LanguageConfig.AutoDetectCulture(CultureInfo.CurrentCulture.Name));
            if (args.Length == 0)
                ApplyResourceCulture(culture);
            else
                ApplyCultureInfo(culture);
            Console.Title = $"UmamusumeResponseAnalyzer v{Assembly.GetExecutingAssembly().GetName().Version}";
            Console.OutputEncoding = Encoding.UTF8;
            Environment.SetEnvironmentVariable("DOTNET_SYSTEM_NET_DISABLEIPV6", "true");
            try
            {
                if (TryHandleCliOnlyArguments(args))
                    return;

                if (!Directory.Exists(WORKING_DIRECTORY)) Directory.CreateDirectory(WORKING_DIRECTORY);
                Directory.SetCurrentDirectory(WORKING_DIRECTORY);

                if (Console.IsInputRedirected || Console.IsOutputRedirected)
                {
                    Console.Error.WriteLine(
                        I18N_RedirectedConsole);
                    Environment.ExitCode = 1;
                    return;
                }

                RunInteractive();
            }
            catch (PostShutdownProcessRequestedException ex)
            {
                Process.Start(ex.StartInfo);
            }
        }

        static void RunInteractive()
        {
            var previousContext = SynchronizationContext.Current;
            Application.MaximumIterationsPerSecond = 50;
            using var application = Application.Create();
            application.Init();
            using var context = new SingleThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                context.Bind(application);
                context.Run(RunInteractiveAsync(application, context));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        static async Task RunInteractiveAsync(
            IApplication application,
            SingleThreadSynchronizationContext synchronizationContext)
        {
            var lifetimeCts = new CancellationTokenSource();
            var lifetimeToken = lifetimeCts.Token;
            UiHost? uiHost = null;
            ShutdownCommandTarget? shutdownTarget = null;
            var shutdownBindingAdded = false;
            ExceptionDispatchInfo? workflowFailure = null;
            try
            {
                Config.Initialize();
                uiHost = new(
                    application,
                    synchronizationContext,
                    lifetimeToken,
                    async () =>
                    {
                        lifetimeCts.Cancel();
                        await RunCleanupAsync(null,
                        [
                            async () => await Server.StopAsync(),
                            async () => await PluginManager.ShutdownAsync(),
                        ]);
                    });
                TerminalUi.Initialize(uiHost);
                var bootstrap = uiHost.Bootstrap;
                shutdownTarget = new(lifetimeCts.Cancel);
                application.Keyboard.KeyBindings.AddApp(
                    Terminal.Gui.Input.Key.C.WithCtrl,
                    shutdownTarget,
                    Terminal.Gui.Input.Command.Quit);
                shutdownBindingAdded = true;
                HotkeyManager.OverlaySink = uiHost;
                bootstrap.SetSettings(ReadBootstrapSettings());
                bootstrap.ShowPreparing();

                async Task CompleteStartupAsync()
                {
                    BootstrapPhaseId? currentPhase = BootstrapPhaseId.Config;
                    try
                    {
                        await uiHost.Ready.WaitAsync(lifetimeToken);
                        await ResourceUpdater.HandleStartupProgramUpdateAsync(lifetimeToken);
                        if (Config.Core.ShowFirstRunPrompt)
                        {
                            try
                            {
                                ShowFirstLaunchPrompt(lifetimeToken);
                            }
                            catch (OperationCanceledException)
                            {
                                lifetimeCts.Cancel();
                                return;
                            }
                            Config.Core.ShowFirstRunPrompt = false;
                            Config.Save();
                        }

                        bootstrap.SetSettings(ReadBootstrapSettings());
                        bootstrap.SetPhase(
                            BootstrapPhaseId.Config,
                            BootstrapPhaseState.Succeeded,
                            string.Format(I18N_ConfigLoaded, Config.CONFIG_FILEPATH));

                        currentPhase = BootstrapPhaseId.Scan;
                        await StartPluginInitializationAsync(bootstrap, lifetimeToken);
                        currentPhase = null;
                        try
                        {
                            await ShowMenu(bootstrap, lifetimeToken);
                        }
                        catch (OperationCanceledException)
                        {
                            lifetimeCts.Cancel();
                            return;
                        }

                        var startupSettings = ReadBootstrapSettings();
                        bootstrap.SetSettings(startupSettings);
                        bootstrap.SetPluginSummary(BuildBootstrapPluginSummary(initialized: false));
                        bootstrap.ShowInformation();
                        await uiHost.FlushAsync();

                        var serverStarted = await Task.Run(async () =>
                        {
                            currentPhase = BootstrapPhaseId.Database;
                            bootstrap.SetPhase(BootstrapPhaseId.Database, BootstrapPhaseState.Running, I18N_LoadingDatabase);
                            var databaseAvailability = Database.Initialize(out var databaseErrorDetails);
                            if (databaseAvailability != DatabaseAvailability.Ready)
                            {
                                var message = I18N_DatabaseUnavailable;
                                bootstrap.SetPhase(BootstrapPhaseId.Database, BootstrapPhaseState.Failed, message, databaseErrorDetails);
                                bootstrap.SetPhase(BootstrapPhaseId.Initialize, BootstrapPhaseState.Skipped, I18N_PluginsSkipped);
                                bootstrap.SetPhase(BootstrapPhaseId.Listener, BootstrapPhaseState.Skipped, I18N_ServerSkipped);
                                bootstrap.SetPhase(BootstrapPhaseId.StartCallbacks, BootstrapPhaseState.Skipped, I18N_PluginCallbacksSkipped);
                                bootstrap.SetPhase(BootstrapPhaseId.Host, BootstrapPhaseState.Failed, message, databaseErrorDetails);
                                bootstrap.Log("Database", message, UiSeverity.Error);
                                Environment.ExitCode = 1;
                                return false;
                            }
                            bootstrap.SetPhase(BootstrapPhaseId.Database, BootstrapPhaseState.Succeeded, I18N_DatabaseLoaded);

                            lifetimeToken.ThrowIfCancellationRequested();
                            currentPhase = BootstrapPhaseId.Initialize;
                            bootstrap.SetPhase(BootstrapPhaseId.Initialize, BootstrapPhaseState.Running, I18N_InitializingPlugins);
                            PluginManager.InitializeLoadedPlugins();
                            bootstrap.SetPluginSummary(BuildBootstrapPluginSummary(initialized: true));
                            var loadedPluginCount = PluginManager.SnapshotActivePluginMetadatas().Count;
                            var failedPluginCount = PluginManager.SnapshotFailures().Count;
                            bootstrap.SetPhase(
                                BootstrapPhaseId.Initialize,
                                failedPluginCount == 0 ? BootstrapPhaseState.Succeeded : BootstrapPhaseState.Warning,
                                failedPluginCount == 0
                                    ? string.Format(I18N_PluginsInitialized, loadedPluginCount)
                                    : string.Format(I18N_PluginsInitializedWithFailures, loadedPluginCount, failedPluginCount));

                            lifetimeToken.ThrowIfCancellationRequested();
                            currentPhase = BootstrapPhaseId.Listener;
                            bootstrap.SetPhase(BootstrapPhaseId.Listener, BootstrapPhaseState.Running, I18N_StartingListener);
                            try
                            {
                                Server.Start(lifetimeToken); //启动HTTP服务器
                            }
                            catch (Exception ex)
                            {
                                bootstrap.SetPhase(BootstrapPhaseId.Listener, BootstrapPhaseState.Failed, ex.Message, ex.ToString());
                                bootstrap.SetPhase(BootstrapPhaseId.StartCallbacks, BootstrapPhaseState.Skipped, I18N_PluginCallbacksSkipped);
                                throw;
                            }

                            bootstrap.Log(
                                "Plugin",
                                loadedPluginCount == 0
                                    ? I18N_NoPluginsHint
                                    : string.Format(I18N_PluginsLoaded, loadedPluginCount),
                                loadedPluginCount == 0 ? UiSeverity.Warning : UiSeverity.Success);
                            foreach (var plugin in PluginManager.FailedPlugins)
                            {
                                var message = string.Format(I18N_PluginLoadFailed, Path.GetFileName(plugin));
                                bootstrap.Log("Plugin", message, UiSeverity.Warning);
                            }

                            for (var i = 0; i < 30; i++)
                            {
                                if (Server.IsRunning) break;
                                await Task.Delay(100, lifetimeToken);
                            }
                            if (!Server.IsRunning)
                            {
                                bootstrap.SetPhase(BootstrapPhaseId.Listener, BootstrapPhaseState.Failed, I18N_ListenerDidNotStart);
                                bootstrap.SetPhase(BootstrapPhaseId.StartCallbacks, BootstrapPhaseState.Skipped, I18N_PluginCallbacksSkipped);
                                bootstrap.SetPhase(BootstrapPhaseId.Host, BootstrapPhaseState.Failed, I18N_ListenerDidNotStart);
                                bootstrap.Log("Server", I18N_ListenerDidNotStart, UiSeverity.Error);
                                Environment.ExitCode = 1;
                                return false;
                            }

                            var endpoint = new UriBuilder(Uri.UriSchemeHttp, startupSettings.ListenHost, startupSettings.ListenPort)
                                .Uri.GetLeftPart(UriPartial.Authority);
                            var listening = string.Format(I18N_ListeningEndpoint, endpoint);
                            bootstrap.SetListening(endpoint);
                            bootstrap.SetPhase(BootstrapPhaseId.Listener, BootstrapPhaseState.Succeeded, listening);
                            bootstrap.Log("Server", listening, UiSeverity.Success);
                            if (startupSettings.ListenHost == "0.0.0.0")
                            {
                                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                                       .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                                       .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                                       .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                                       .Select(x => x.Address.ToString());
                                foreach (var i in interfaces)
                                {
                                    bootstrap.Log("Server", string.Format(Localization.Server.I18N_AvailableEndpointTip, i, startupSettings.ListenPort));
                                }
                            }

                            return true;
                        }, lifetimeToken);

                        if (!serverStarted)
                            return;

                        currentPhase = BootstrapPhaseId.StartCallbacks;
                        bootstrap.SetPhase(BootstrapPhaseId.StartCallbacks, BootstrapPhaseState.Running, I18N_StartingPluginCallbacks);
                        await PluginManager.TriggerStartedAsync(lifetimeToken);
                        bootstrap.SetPluginSummary(BuildBootstrapPluginSummary(initialized: true, started: true));
                        var startupFailures = PluginManager.SnapshotFailures();
                        var startFailures = startupFailures.Count(failure => failure.Stage == PluginFailureStage.Start);
                        bootstrap.SetPhase(
                            BootstrapPhaseId.StartCallbacks,
                            startFailures == 0 ? BootstrapPhaseState.Succeeded : BootstrapPhaseState.Warning,
                            startFailures == 0
                                ? string.Format(I18N_PluginCallbacksCompleted, PluginManager.SnapshotActivePluginMetadatas().Count)
                                : string.Format(I18N_PluginCallbacksWithFailures, startFailures));
                        var startedMessage = startupFailures.Count == 0
                            ? I18N_Start_Started
                            : string.Format(I18N_StartWithPluginFailures, startupFailures.Count);
                        bootstrap.Log("URA", startedMessage, startupFailures.Count == 0 ? UiSeverity.Success : UiSeverity.Warning);
                        bootstrap.SetPhase(BootstrapPhaseId.Host,
                            startupFailures.Count == 0 ? BootstrapPhaseState.Succeeded : BootstrapPhaseState.Warning,
                            startedMessage);
                        currentPhase = null;
                        _ = CheckPluginUpdatesAsync(uiHost, lifetimeToken);
                    }
                    catch (PostShutdownProcessRequestedException ex)
                    {
                        workflowFailure = ExceptionDispatchInfo.Capture(ex);
                        lifetimeCts.Cancel();
                    }
                    catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        workflowFailure = ExceptionDispatchInfo.Capture(ex);
                        Environment.ExitCode = 1;
                        if (lifetimeCts.IsCancellationRequested)
                            return;
                        try
                        {
                            bootstrap.ShowInformation();
                            if (currentPhase is { } failedPhase)
                                bootstrap.SetPhase(failedPhase, BootstrapPhaseState.Failed, ex.Message, ex.ToString());
                            foreach (var phase in Enum.GetValues<BootstrapPhaseId>().Where(phase =>
                                         phase > (currentPhase ?? BootstrapPhaseId.Scan) && phase < BootstrapPhaseId.Host))
                                bootstrap.SetPhase(phase, BootstrapPhaseState.Skipped, I18N_StartupStepSkipped);
                            bootstrap.SetPhase(BootstrapPhaseId.Host, BootstrapPhaseState.Failed, ex.Message, ex.ToString());
                            TerminalUi.LogException("URA", ex);
                            await uiHost.FlushAsync();
                        }
                        catch
                        {
                            uiHost.RequestShutdown();
                            throw;
                        }
                    }
                }

                // RunAsync 会同步进入 run loop，必须先创建 startup waiter。
                _ = CompleteStartupAsync();
                await uiHost.RunAsync();
            }
            catch (Exception ex)
            {
                workflowFailure ??= ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                await RunCleanupAsync(
                    workflowFailure,
                    [
                        () =>
                        {
                            lifetimeCts.Cancel();
                            return ValueTask.CompletedTask;
                        },
                        async () =>
                        {
                            if (uiHost is not null)
                                await uiHost.ShutdownAsync();
                        },
                        () =>
                        {
                            uiHost?.Dispose();
                            return ValueTask.CompletedTask;
                        },
                        () =>
                        {
                            if (shutdownBindingAdded)
                                application.Keyboard.KeyBindings.Remove(Terminal.Gui.Input.Key.C.WithCtrl);
                            return ValueTask.CompletedTask;
                        },
                        () =>
                        {
                            synchronizationContext.Unbind(application);
                            return ValueTask.CompletedTask;
                        },
                        () =>
                        {
                            shutdownTarget?.Dispose();
                            return ValueTask.CompletedTask;
                        },
                        () =>
                        {
                            lifetimeCts.Dispose();
                            return ValueTask.CompletedTask;
                        },
                    ]);
            }
        }

        internal static async Task RunCleanupAsync(
            ExceptionDispatchInfo? workflowFailure,
            IReadOnlyList<Func<ValueTask>> cleanupActions,
            string? aggregateMessage = null)
        {
            List<Exception>? cleanupFailures = null;
            foreach (var cleanup in cleanupActions)
            {
                try
                {
                    await cleanup();
                }
                catch (Exception ex)
                {
                    (cleanupFailures ??= []).Add(ex);
                }
            }

            workflowFailure?.Throw();
            if (cleanupFailures is [var cleanupFailure])
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            if (cleanupFailures is { Count: > 1 })
                throw new AggregateException(aggregateMessage ?? I18N_HostCleanupFailed, cleanupFailures);
        }

        static async Task StartPluginInitializationAsync(BootstrapWorkspace bootstrap, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bootstrap.SetPhase(BootstrapPhaseId.Scan, BootstrapPhaseState.Running, I18N_ScanningPlugins);
            try
            {
                await Task.Run(() => PluginManager.Init(cancellationToken), cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                bootstrap.SetPhase(BootstrapPhaseId.Scan, BootstrapPhaseState.Failed, ex.Message, ex.ToString());
                bootstrap.SetPluginSummary(BuildBootstrapPluginSummary(initialized: false));
                TerminalUi.LogException("Plugin", ex);
                throw;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var loadedPluginCount = PluginManager.SnapshotActivePluginMetadatas().Count;
            var failedPluginCount = PluginManager.SnapshotFailures().Count;
            bootstrap.SetPhase(
                BootstrapPhaseId.Scan,
                failedPluginCount == 0 ? BootstrapPhaseState.Succeeded : BootstrapPhaseState.Warning,
                failedPluginCount == 0
                    ? string.Format(I18N_PluginsFound, loadedPluginCount)
                    : string.Format(I18N_PluginsFoundWithFailures, loadedPluginCount, failedPluginCount));
            bootstrap.SetPluginSummary(BuildBootstrapPluginSummary(initialized: false));
        }

        static IReadOnlyList<BootstrapPluginRow> BuildBootstrapPluginSummary(bool initialized, bool started = false)
        {
            var metadatas = PluginManager.Metadatas;
            var loadedNames = PluginManager.SnapshotActivePluginMetadatas()
                .Select(plugin => plugin.PluginName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var failures = PluginManager.SnapshotFailures()
                .ToDictionary(failure => failure.PackagePath, StringComparer.OrdinalIgnoreCase);
            List<BootstrapPluginRow> rows = [];
            foreach (var metadata in metadatas.Values)
            {
                failures.Remove(metadata.PackagePath, out var failure);
                var active = loadedNames.Contains(metadata.PluginName);
                var state = failure is { DisablesPlugin: true } || !active
                    ? BootstrapPluginState.Unavailable
                    : failure is not null
                        ? BootstrapPluginState.StartFailed
                        : initialized ? BootstrapPluginState.Active : BootstrapPluginState.Scanned;
                rows.Add(new(
                    metadata.PackagePath,
                    metadata.DisplayName == metadata.PluginName
                        ? metadata.DisplayName
                        : $"{metadata.DisplayName} ({metadata.PluginName})",
                    metadata.Version.ToString(),
                    state,
                    failure?.Error.Message ?? (active
                        ? started ? I18N_Start_Started : initialized ? I18N_Initialized : I18N_Scanned
                        : I18N_Unloaded),
                    failure?.Error.ToString(),
                    failure?.Stage));
            }
            foreach (var failure in failures.Values)
                rows.Add(new(
                    failure.PackagePath,
                    failure.PluginName ?? Path.GetFileNameWithoutExtension(failure.PackagePath),
                    string.Empty,
                    BootstrapPluginState.Unavailable,
                    failure.Error.Message,
                    failure.Error.ToString(),
                    failure.Stage));
            return
            [
                .. rows
                    .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            ];
        }

        static async Task CheckPluginUpdatesAsync(UiHost uiHost, CancellationToken cancellationToken)
        {
            try
            {
                var updates = await PluginRepository.CheckForUpdatesAsync(cancellationToken);
                if (updates.Count == 0)
                    return;

                uiHost.Notify(
                    null,
                    $"[URA] {FormatPluginUpdateNotification(updates)}",
                    UiSeverity.Info,
                    TimeSpan.FromSeconds(12),
                    []);

                foreach (var update in updates)
                {
                    uiHost.Log(
                        $"[URA] {string.Format(I18N_PluginUpdate, update.DisplayName, update.CurrentVersion, update.LatestVersion)}",
                        UiSeverity.Info);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                TerminalUi.LogException("URA", ex, UiSeverity.Warning);
                uiHost.Notify(
                    null,
                    string.Format(I18N_UpdateCheckFailed, ex.Message),
                    UiSeverity.Warning,
                    ttl: null,
                    []);
            }
        }

        static string FormatPluginUpdateNotification(IReadOnlyList<PluginUpdateInfo> updates)
        {
            if (updates.Count == 1)
            {
                var update = updates[0];
                return string.Format(I18N_PluginUpdate, update.DisplayName, update.CurrentVersion, update.LatestVersion);
            }

            var names = string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ", updates.Take(3).Select(x => x.DisplayName));
            return string.Format(updates.Count > 3 ? I18N_PluginUpdatesMore : I18N_PluginUpdates, updates.Count, names);
        }

        static void ShowFirstLaunchPrompt(CancellationToken cancellationToken)
        {
            var allowOtherDevices = ModalDialogs.Select(
                I18N_FirstRunDevice,
                new[] { true, false },
                value => value ? I18N_MobileAndPc : I18N_ThisPc,
                cancellationToken);
            if (allowOtherDevices)
                Config.Core.ListenAddress = "0.0.0.0";
            Config.Updater.DatabaseLanguage = ModalDialogs.Select(
                $"{(allowOtherDevices ? I18N_ExternalNetworkNotice : I18N_LocalNetworkNotice)}\n\n{I18N_FirstRunDataLanguage}",
                new[] { "ja-JP", "zh-TW" },
                value => value == "ja-JP" ? I18N_Japanese : I18N_TraditionalChinese,
                cancellationToken);
            Config.Updater.TrainerIsMale = ModalDialogs.Select(
                I18N_FirstRunTrainerGender,
                new[] { true, false },
                value => value ? I18N_Male : I18N_Female,
                cancellationToken);
            ModalDialogs.Acknowledge(I18N_FirstRunComplete, cancellationToken);
        }
        static BootstrapSettings ReadBootstrapSettings() => new(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? I18N_Unknown,
            Directory.GetCurrentDirectory(),
            Path.GetFullPath(Config.CONFIG_FILEPATH),
            Config.Core.ListenAddress,
            Config.Core.ListenPort,
            Config.Updater.DatabaseLanguage,
            Config.Updater.TrainerIsMale ? I18N_Male : I18N_Female,
            string.IsNullOrWhiteSpace(Config.Updater.CustomDatabaseRepository)
                ? "https://github.com/UmamusumeResponseAnalyzer/Assets/raw/refs/heads/main/".AllowMirror()
                : Config.Updater.CustomDatabaseRepository);

        static async Task ShowMenu(BootstrapWorkspace bootstrap, CancellationToken cancellationToken)
        {
            while (true)
            {
                bootstrap.SetSettings(ReadBootstrapSettings());
                bootstrap.SetPluginSummary(BuildBootstrapPluginSummary(initialized: false));
                var selected = await bootstrap.ShowMenuAsync(cancellationToken);
                if (selected == BootstrapAction.Start)
                    return;

                try
                {
                    switch (selected)
                    {
                        case BootstrapAction.Options:
                            await Config.PromptAsync(cancellationToken);
                            break;
                        case BootstrapAction.PluginRepository:
                            await PluginRepository.ShowMenuAsync(cancellationToken);
                            break;
                        case BootstrapAction.UpdateData:
                            await ResourceUpdater.UpdateAssets(cancellationToken);
                            break;
                        case BootstrapAction.UpdateProgram:
                            await ResourceUpdater.UpdateProgram(cancellationToken);
                            break;
                        case BootstrapAction.Hachimi:
                            await HachimiEdgeInstaller.ShowAsync(cancellationToken);
                            break;
                        case BootstrapAction.QqGroup:
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "https://qm.qq.com/q/4z6xHQ908w",
                                UseShellExecute = true
                            });
                            ModalDialogs.Acknowledge(I18N_QqOpened, cancellationToken);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(selected), selected, null);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        static bool TryHandleCliOnlyArguments(string[] args)
        {
            switch (args)
            {
                case ["-v" or "--version"]:
                    Console.Write(Assembly.GetExecutingAssembly().GetName().Version);
                    return true;
                case ["--update", var savePath]:
                    ResourceUpdater.InstallProgramUpdate(savePath);
                    return true;
                case ["--apply-hachimi-edge", var requestPath, "--confirmed", "--culture", "zh-CN" or "en-US" or "ja-JP"]:
                    ApplyCultureInfo(CultureInfo.GetCultureInfo(args[4]));
                    Environment.ExitCode = HachimiEdgeInstallation.RunApplyCommand(requestPath);
                    return true;
                case ["--apply-hachimi-edge", ..]:
                    Console.Error.WriteLine(I18N_InvalidInstallCommand);
                    Environment.ExitCode = 1;
                    return true;
                case ["--enable-dll-redirection", "--confirmed"]:
                    UraCoreHelper.EnableDllRedirection();
                    return true;
                case ["--enable-dll-redirection"]:
                    Console.Error.WriteLine(
                        I18N_RegistryConfirmationRequired);
                    Environment.ExitCode = 1;
                    return true;
                case []:
                    return false;
                default:
                    Console.Error.WriteLine(string.Format(I18N_UnknownArguments, string.Join(' ', args)));
                    Environment.ExitCode = 2;
                    return true;
            }
        }
        internal static void ApplyCultureInfo(CultureInfo? culture = null)
        {
            culture ??= CultureInfo.GetCultureInfo(LanguageConfig.GetCulture());
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            ApplyResourceCulture(culture);
        }

        internal static void ApplyResourceCulture(CultureInfo culture)
        {
            foreach (var i in Assembly.GetExecutingAssembly().GetTypes().Where(x => x.IsClass && x.Namespace?.StartsWith("UmamusumeResponseAnalyzer.Localization") == true))
            {
                var rc = i?.GetField("resourceCulture", BindingFlags.NonPublic | BindingFlags.Static);
                if (rc == null) continue;
                rc.SetValue(null, culture);
            }
            Database.RefreshLocalizedText();
        }
        internal static void Restart()
        {
            var exePath = Environment.ProcessPath!;
            StartAfterTerminalCleanup(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)!
            });
        }

        internal static void StartAfterTerminalCleanup(ProcessStartInfo startInfo)
            => throw new PostShutdownProcessRequestedException(startInfo);

        internal sealed class PostShutdownProcessRequestedException(ProcessStartInfo startInfo) : Exception
        {
            public ProcessStartInfo StartInfo { get; } = startInfo;
        }

        sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
        {
            readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> workItems = [];
            readonly object lifecycleGate = new();
            readonly int ownerThreadId = Environment.CurrentManagedThreadId;
            IApplication? application;
            int applicationDrainScheduled;
            int closing;

            public void Bind(IApplication value)
            {
                ArgumentNullException.ThrowIfNull(value);
                if (Environment.CurrentManagedThreadId != ownerThreadId)
                    throw new InvalidOperationException(I18N_BindOwnerThread);
                if (application is not null)
                    throw new InvalidOperationException(I18N_AlreadyBound);
                if (value.MainThreadId != ownerThreadId)
                    throw new InvalidOperationException(I18N_MainThreadMismatch);

                application = value;
                value.Iteration += ApplicationIteration;
            }

            public void Unbind(IApplication value)
            {
                if (Environment.CurrentManagedThreadId != ownerThreadId)
                    throw new InvalidOperationException(I18N_UnbindOwnerThread);
                if (!ReferenceEquals(application, value))
                    throw new InvalidOperationException(I18N_BindingMismatch);

                value.Iteration -= ApplicationIteration;
                application = null;
                Interlocked.Exchange(ref applicationDrainScheduled, 0);
            }

            public override void Post(SendOrPostCallback callback, object? state)
            {
                ArgumentNullException.ThrowIfNull(callback);
                lock (lifecycleGate)
                {
                    if (closing != 0)
                        return;
                    workItems.Add((callback, state));
                }
                WakeApplicationLoop();
            }

            public override void Send(SendOrPostCallback callback, object? state)
            {
                ArgumentNullException.ThrowIfNull(callback);
                if (Environment.CurrentManagedThreadId == ownerThreadId)
                {
                    callback(state);
                    return;
                }

                ExceptionDispatchInfo? failure = null;
                using var completed = new ManualResetEventSlim();
                Post(_ =>
                {
                    try
                    {
                        callback(state);
                    }
                    catch (Exception ex)
                    {
                        failure = ExceptionDispatchInfo.Capture(ex);
                    }
                    finally
                    {
                        completed.Set();
                    }
                }, null);
                completed.Wait();
                failure?.Throw();
            }

            public override SynchronizationContext CreateCopy() => this;

            void WakeApplicationLoop()
            {
                var app = Volatile.Read(ref application);
                if (app is null || Interlocked.Exchange(ref applicationDrainScheduled, 1) != 0)
                {
                    return;
                }

                try
                {
                    app.Invoke(static () => { });
                }
                catch (NotInitializedException)
                {
                    Interlocked.Exchange(ref applicationDrainScheduled, 0);
                }
                catch (ObjectDisposedException)
                {
                    Interlocked.Exchange(ref applicationDrainScheduled, 0);
                }
            }

            void ApplicationIteration(
                object? sender,
                Terminal.Gui.App.EventArgs<IApplication?> e)
                => DrainAvailableOnOwner();

            void DrainAvailableOnOwner()
            {
                if (Environment.CurrentManagedThreadId != ownerThreadId)
                    throw new InvalidOperationException(I18N_DrainOwnerThread);

                // 宿主续体使用自身队列，避免嵌套弹窗进入 Terminal.Gui 的 Invoke 计时器锁。
                var previousContext = Current;
                SetSynchronizationContext(this);
                try
                {
                    while (workItems.TryTake(out var workItem))
                        workItem.Callback(workItem.State);
                }
                finally
                {
                    SetSynchronizationContext(previousContext);
                }
                Interlocked.Exchange(ref applicationDrainScheduled, 0);
                if (workItems.Count > 0)
                    WakeApplicationLoop();
            }

            public void Run(Task task)
            {
                ArgumentNullException.ThrowIfNull(task);
                if (Environment.CurrentManagedThreadId != ownerThreadId)
                    throw new InvalidOperationException(I18N_PumpOwnerThread);

                _ = task.ContinueWith(
                    static (_, state) =>
                    {
                        var context = (SingleThreadSynchronizationContext)state!;
                        lock (context.lifecycleGate)
                        {
                            if (context.closing != 0)
                                return;
                            context.closing = 1;
                            context.workItems.CompleteAdding();
                        }
                    },
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                foreach (var workItem in workItems.GetConsumingEnumerable())
                {
                    workItem.Callback(workItem.State);
                    DrainAvailableOnOwner();
                }

                task.GetAwaiter().GetResult();
            }

            public void Dispose()
            {
                lock (lifecycleGate)
                {
                    if (closing == 0)
                    {
                        closing = 1;
                        workItems.CompleteAdding();
                    }
                }
                workItems.Dispose();
            }
        }

        sealed class ShutdownCommandTarget : Terminal.Gui.ViewBase.View
        {
            public ShutdownCommandTarget(Action shutdown)
            {
                AddCommand(Terminal.Gui.Input.Command.Quit, () =>
                {
                    shutdown();
                    return true;
                });
            }
        }
    }
}
