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
                    lifetimeToken);
                uiHost.ShutdownStarting += lifetimeCts.Cancel;
                TerminalUi.Initialize(uiHost);
                var bootstrap = uiHost.Bootstrap;
                shutdownTarget = new(lifetimeCts.Cancel);
                application.Keyboard.KeyBindings.AddApp(
                    Terminal.Gui.Input.Key.C.WithCtrl,
                    shutdownTarget,
                    Terminal.Gui.Input.Command.Quit);
                shutdownBindingAdded = true;
                HotkeyManager.OverlaySink = uiHost;
                bootstrap.ShowPreparingMenu(I18N_Instruction, BuildStartupMenuChoices());

                async Task CompleteStartupAsync()
                {
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

                        var updateSource = string.IsNullOrWhiteSpace(Config.Updater.CustomDatabaseRepository)
                            ? "https://github.com/UmamusumeResponseAnalyzer/Assets/raw/refs/heads/main/".AllowMirror()
                            : Config.Updater.CustomDatabaseRepository;
                        bootstrap.SetSettings(
                            [
                                (I18N_Version, Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? I18N_Unknown),
                                (I18N_WorkingDirectory, Directory.GetCurrentDirectory()),
                                (I18N_ConfigFile, Path.GetFullPath(Config.CONFIG_FILEPATH)),
                                (I18N_ListenAddress, $"http://{Config.Core.ListenAddress}:{Config.Core.ListenPort}"),
                                (I18N_ServerTargets, Config.Repository.Targets.Count == 0 ? I18N_Unrestricted : string.Join(", ", Config.Repository.Targets)),
                                (I18N_DataLanguage, Config.Updater.DatabaseLanguage),
                                (I18N_TrainerGender, Config.Updater.TrainerIsMale ? I18N_Male : I18N_Female),
                                (I18N_UpdateSource, updateSource)
                            ]);
                        bootstrap.SetPhase(
                            "config",
                            I18N_PhaseConfig,
                            UiSeverity.Success,
                            string.Format(I18N_ConfigLoaded, Config.CONFIG_FILEPATH));

                        await StartPluginInitializationAsync(bootstrap, lifetimeToken);
                        try
                        {
                            await ShowMenu(bootstrap, lifetimeToken);
                        }
                        catch (OperationCanceledException)
                        {
                            lifetimeCts.Cancel();
                            return;
                        }

                        bootstrap.SetPluginSummary(BuildBootstrapPluginSummary(initialized: false));
                        bootstrap.ShowInformation();
                        await uiHost.FlushAsync();

                        var serverStarted = await Task.Run(async () =>
                        {
                            bootstrap.SetPhase("database", I18N_PhaseDatabase, UiSeverity.Info, I18N_LoadingDatabase);
                            var databaseAvailability = await Database.Initialize();
                            if (databaseAvailability != DatabaseAvailability.Ready)
                            {
                                var message = I18N_DatabaseUnavailable;
                                bootstrap.SetPhase("database", I18N_PhaseDatabase, UiSeverity.Error, message);
                                bootstrap.SetPhase("plugin-init", I18N_PhasePluginInit, UiSeverity.Error, I18N_PluginsSkipped);
                                bootstrap.SetPhase("server", I18N_PhaseServer, UiSeverity.Error, I18N_ServerSkipped);
                                bootstrap.Log("Database", message, UiSeverity.Error);
                                Environment.ExitCode = 1;
                                return false;
                            }
                            bootstrap.SetPhase("database", I18N_PhaseDatabase, UiSeverity.Success, I18N_DatabaseLoaded);

                            lifetimeToken.ThrowIfCancellationRequested();
                            bootstrap.SetPhase("plugin-init", I18N_PhasePluginInit, UiSeverity.Info, I18N_InitializingPlugins);
                            PluginManager.InitializeLoadedPlugins();
                            bootstrap.SetPluginSummary(BuildBootstrapPluginSummary(initialized: true));
                            var loadedPluginCount = PluginManager.SnapshotPluginStatuses().Count(plugin => plugin.IsLoaded);
                            var failedPluginCount = PluginManager.FailedPlugins.Count;
                            bootstrap.SetPhase(
                                "plugin-init",
                                I18N_PhasePluginInit,
                                failedPluginCount == 0 ? UiSeverity.Success : UiSeverity.Warning,
                                failedPluginCount == 0
                                    ? string.Format(I18N_PluginsInitialized, loadedPluginCount)
                                    : string.Format(I18N_PluginsInitializedWithFailures, loadedPluginCount, failedPluginCount));

                            lifetimeToken.ThrowIfCancellationRequested();
                            bootstrap.SetPhase("server", I18N_PhaseServer, UiSeverity.Info, I18N_StartingListener);
                            try
                            {
                                Server.Start(lifetimeToken); //启动HTTP服务器
                                bootstrap.SetPhase("server", I18N_PhaseServer, UiSeverity.Success, string.Format(I18N_Listening, Config.Core.ListenAddress, Config.Core.ListenPort));
                            }
                            catch (Exception ex)
                            {
                                bootstrap.SetPhase("server", I18N_PhaseServer, UiSeverity.Error, ex.Message);
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

                            bootstrap.Log("Server", string.Format(I18N_Listening, Config.Core.ListenAddress, Config.Core.ListenPort), UiSeverity.Success);
                            if (Config.Core.ListenAddress == "0.0.0.0")
                            {
                                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                                       .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                                       .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                                       .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                                       .Select(x => x.Address.ToString());
                                foreach (var i in interfaces)
                                {
                                    bootstrap.Log("Server", string.Format(Localization.Server.I18N_AvailableEndpointTip, i, Config.Core.ListenPort));
                                }
                            }

                            for (var i = 0; i < 30; i++)
                            {
                                if (Server.IsRunning) break;
                                await Task.Delay(100, lifetimeToken);
                            }
                            if (!Server.IsRunning)
                            {
                                bootstrap.SetPhase("server", I18N_PhaseServer, UiSeverity.Error, I18N_LaunchFail);
                                Console.Error.WriteLine(I18N_LaunchFail);
                                Environment.ExitCode = 1;
                                return false;
                            }

                            var startedMessage = I18N_Start_Started;
                            bootstrap.Log("URA", startedMessage, UiSeverity.Success);
                            bootstrap.SetPhase("host", I18N_PhaseHost, UiSeverity.Success, startedMessage);
                            return true;
                        }, lifetimeToken);

                        if (!serverStarted)
                            return;

                        HotkeyManager.Register(ConsoleKey.P, I18N_PluginList, ctx =>
                        {
                            var plugins = PluginManager.SnapshotPluginStatuses()
                                .Where(plugin => plugin.IsLoaded)
                                .ToArray();
                            foreach (var plugin in plugins)
                                ctx.AddLine(string.Format(I18N_PluginAuthor, plugin.DisplayName, plugin.Version, plugin.Author));
                            if (plugins.Length == 0)
                                ctx.AddLine(I18N_NoPlugins);
                            return Task.CompletedTask;
                        });
                        await PluginManager.TriggerStartedAsync(lifetimeToken);
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
                            bootstrap.SetPhase("host", I18N_PhaseHost, UiSeverity.Error, ex.Message);
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

                // Terminal.Gui 2.4.17 的 RunAsync 会同步进入 run loop，必须先创建 startup waiter。
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
                        async () => await Server.StopAsync(),
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
            bootstrap.SetPhase("plugin-scan", I18N_PhasePluginScan, UiSeverity.Info, I18N_ScanningPlugins);
            try
            {
                await Task.Run(() => PluginManager.Init(cancellationToken), cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                bootstrap.SetPhase("plugin-scan", I18N_PhasePluginScan, UiSeverity.Error, ex.Message);
                TerminalUi.LogException("Plugin", ex);
                throw;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var loadedPluginCount = PluginManager.SnapshotPluginStatuses().Count(plugin => plugin.IsLoaded);
            var failedPluginCount = PluginManager.FailedPlugins.Count;
            bootstrap.SetPhase(
                "plugin-scan",
                I18N_PhasePluginScan,
                failedPluginCount == 0 ? UiSeverity.Success : UiSeverity.Warning,
                failedPluginCount == 0
                    ? string.Format(I18N_PluginsFound, loadedPluginCount)
                    : string.Format(I18N_PluginsFoundWithFailures, loadedPluginCount, failedPluginCount));
            bootstrap.SetPluginSummary(BuildBootstrapPluginSummary(initialized: false));
        }

        static IReadOnlyList<BootstrapPluginRow> BuildBootstrapPluginSummary(bool initialized)
        {
            var statuses = PluginManager.SnapshotPluginStatuses();
            var failedPlugins = PluginManager.FailedPlugins.ToArray();
            var pluginNamesByPath = PluginManager.Metadatas.Values
                .GroupBy(x => x.PackagePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().PluginName,
                    StringComparer.OrdinalIgnoreCase);
            var failedNames = failedPlugins
                .Select(path => pluginNamesByPath.GetValueOrDefault(path) ?? path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var knownNames = statuses
                .SelectMany(x => new[] { x.InternalName, x.DisplayName })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<(string SortKey, BootstrapPluginRow Row)> rows =
            [
                .. statuses.Select(status => (
                    status.InternalName,
                    new BootstrapPluginRow(
                        status,
                        initialized,
                        failedNames.Contains(status.InternalName) ||
                        failedNames.Contains(status.DisplayName))))
            ];

            foreach (var failedPath in failedPlugins)
            {
                var name = pluginNamesByPath.GetValueOrDefault(failedPath)
                    ?? Path.GetFileNameWithoutExtension(failedPath);
                if (knownNames.Add(name))
                    rows.Add((name, new(name, string.Empty, BootstrapWorkspace.SeverityLabel(UiSeverity.Error), I18N_ScanLoadFailed)));
            }

            return
            [
                .. rows
                    .OrderBy(x => x.SortKey, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Row)
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
            Config.Repository.Targets.AddRange(ModalDialogs.MultiSelect(
                allowOtherDevices ? I18N_ExternalNetworkNotice : I18N_LocalNetworkNotice,
                new[] { "Cygames", "Komoe" },
                converter: value => value == "Cygames" ? I18N_Cygames : I18N_Komoe,
                cancellationToken: cancellationToken));
            Config.Updater.DatabaseLanguage = ModalDialogs.Select(
                I18N_FirstRunDataLanguage,
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
        static List<string> BuildStartupMenuChoices()
        {
            var selections = new List<string>
            {
                I18N_Start,
                I18N_Options,
                I18N_PluginRepository,
                I18N_UpdateAssets,
                I18N_UpdateProgram,
                I18N_QqGroup
            };
            if (OperatingSystem.IsWindows())
                selections.Add(I18N_InstallUraCore);
            return selections;
        }

        static async Task ShowMenu(BootstrapWorkspace bootstrap, CancellationToken cancellationToken)
        {
            while (true)
            {
                var selected = await bootstrap.ShowMenuAsync(
                    I18N_Instruction,
                    BuildStartupMenuChoices(),
                    cancellationToken: cancellationToken);
                if (selected == I18N_Start)
                    return;

                try
                {
                    if (selected == I18N_Options)
                    {
                        await Config.PromptAsync(cancellationToken);
                    }
                    else if (selected == I18N_PluginRepository)
                    {
                        await PluginRepository.ShowMenuAsync(cancellationToken);
                    }
                    else if (selected == I18N_UpdateAssets)
                    {
                        await ResourceUpdater.UpdateAssets(cancellationToken);
                    }
                    else if (selected == I18N_UpdateProgram)
                    {
                        await ResourceUpdater.UpdateProgram(cancellationToken);
                    }
                    else if (selected == I18N_InstallUraCore)
                    {
                        await HachimiEdgeInstaller.ShowAsync(cancellationToken);
                    }
                    else if (selected == I18N_QqGroup)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://qm.qq.com/q/4z6xHQ908w",
                            UseShellExecute = true
                        });
                        ModalDialogs.Acknowledge(
                            I18N_QqOpened,
                            cancellationToken);
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

                while (workItems.TryTake(out var workItem))
                    workItem.Callback(workItem.State);
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
