using Spectre.Console;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;
using static UmamusumeResponseAnalyzer.Localization.LaunchMenu;

namespace UmamusumeResponseAnalyzer
{
    public static class UmamusumeResponseAnalyzer
    {
        internal static Task _database_initialize_task = null!;
        internal static Task _plugin_initialize_task = null!;
        public static bool Started => Server.IsRunning;
        const string PORTABLE_WORKING_DIRECTORY = "./.portable";
        public readonly static string WORKING_DIRECTORY = Directory.Exists(PORTABLE_WORKING_DIRECTORY) ? PORTABLE_WORKING_DIRECTORY : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer");
        public static async Task Main(string[] args)
        {
            Console.Title = $"UmamusumeResponseAnalyzer v{Assembly.GetExecutingAssembly().GetName().Version}";
            Console.OutputEncoding = Encoding.UTF8;
            Environment.SetEnvironmentVariable("DOTNET_SYSTEM_NET_DISABLEIPV6", "true");
            if (!Directory.Exists(WORKING_DIRECTORY)) Directory.CreateDirectory(WORKING_DIRECTORY);
            Directory.SetCurrentDirectory(WORKING_DIRECTORY);
            await ParseArguments(args);

            Config.Initialize();
            if (Config.Core.ShowFirstRunPrompt)
            {
                LiveDisplayConsole.Run(ShowFirstLaunchPrompt);
                Config.Core.ShowFirstRunPrompt = false;
                Config.Save();
            }

            var uiHost = new UiHost();
            var bootstrap = new BootstrapWorkspace(uiHost);
            LiveDisplayConsole.Bind(uiHost);
            LiveDisplayConsole.DefaultLogWorkspace = bootstrap.Workspace;
            KeyboardManager.OverlaySink = uiHost;
            PluginManager.BindLiveDisplay(plugin => uiHost.ForPlugin(plugin.Name));

            bootstrap.SetSettings(BuildBootstrapSettings());
            bootstrap.SetPhase("config", "配置", LiveDisplaySeverity.Success, "已读取 config.yaml");

            _plugin_initialize_task = StartPluginInitializationAsync(bootstrap);
            var prompt = string.Empty;
            do
            {
                prompt = await LiveDisplayConsole.RunAsync(ShowMenu);
            }
            while (prompt != I18N_Start); //如果不是启动则重新显示主菜单

            bootstrap.SetPhase("database", "数据文件", LiveDisplaySeverity.Info, "正在加载事件、技能、名称等数据。");
            _database_initialize_task = Database.Initialize();
            Task.WaitAll(_database_initialize_task, _plugin_initialize_task); //等待数据库初始化完成
            bootstrap.SetPhase("database", "数据文件", LiveDisplaySeverity.Success, "加载完成；缺失或损坏项见日志。");

            bootstrap.SetPhase("plugin-init", "插件初始化", LiveDisplaySeverity.Info, "正在调用插件 Initialize。");
            PluginManager.InitializeLoadedPlugins();
            var loadedPluginCount = PluginManager.LoadedPlugins.Count;
            var failedPluginCount = PluginManager.FailedPlugins.Count;
            bootstrap.SetPhase(
                "plugin-init",
                "插件初始化",
                failedPluginCount == 0 ? LiveDisplaySeverity.Success : LiveDisplaySeverity.Warning,
                failedPluginCount == 0
                    ? $"已初始化 {loadedPluginCount} 个插件。"
                    : $"已初始化 {loadedPluginCount} 个插件，{failedPluginCount} 个插件失败。");

            bootstrap.SetPhase("server", "HTTP server", LiveDisplaySeverity.Info, "正在启动监听。");
            try
            {
                Server.Start(); //启动HTTP服务器
                bootstrap.SetPhase("server", "HTTP server", LiveDisplaySeverity.Success, $"监听 http://{Config.Core.ListenAddress}:{Config.Core.ListenPort}");
            }
            catch (Exception ex)
            {
                bootstrap.SetPhase("server", "HTTP server", LiveDisplaySeverity.Error, ex.Message);
                throw;
            }

            bootstrap.Log(
                "Plugin",
                loadedPluginCount == 0
                    ? "没有加载任何插件。可从插件仓库安装插件。"
                    : $"已加载 {loadedPluginCount} 个插件。按 P 查看插件列表。",
                loadedPluginCount == 0 ? LiveDisplaySeverity.Warning : LiveDisplaySeverity.Success);
            foreach (var plugin in PluginManager.FailedPlugins)
            {
                var message = $"插件 {Path.GetFileName(plugin)} 加载失败";
                bootstrap.Log("Plugin", message, LiveDisplaySeverity.Warning);
            }

            bootstrap.Log("Server", $"监听 http://{Config.Core.ListenAddress}:{Config.Core.ListenPort}", LiveDisplaySeverity.Success);
            if (Config.Core.ListenAddress == "0.0.0.0")
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                       .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                       .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                       .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                       .Select(x => x.Address.ToString())
                       .ToList();
                foreach (var i in interfaces)
                {
                    bootstrap.Log("Server", string.Format(Localization.Server.I18N_AvailableEndpointTip, i, Config.Core.ListenPort));
                }
            }

            for (var i = 0; i < 30; i++)
            {
                if (Server.IsRunning) break;
                await Task.Delay(100);
            }
            if (!Server.IsRunning)
            {
                bootstrap.SetPhase("server", "HTTP server", LiveDisplaySeverity.Error, I18N_LaunchFail.RemoveMarkup());
                LiveDisplayConsole.WriteLine(I18N_LaunchFail);
                LiveDisplayConsole.ReadLine();
                Environment.Exit(1);
            }

            var startedMessage = I18N_Start_Started.RemoveMarkup();
            bootstrap.Log("URA", startedMessage, LiveDisplaySeverity.Success);
            bootstrap.SetPhase("started", "宿主", LiveDisplaySeverity.Success, startedMessage);

            await PluginManager.TriggerStartedAsync();

            _ = Task.Run(() => CheckPluginUpdatesAsync(uiHost));

            KeyboardManager.Register(
                ConsoleKey.C, ConsoleModifiers.Control,
                "退出程序",
                () =>
                {
                    KeyboardManager.Stop();
                    uiHost.RequestShutdown();
                    return Task.CompletedTask;
            });
            KeyboardManager.Register(ConsoleKey.P, "插件列表", ctx =>
            {
                var plugins = PluginManager.SnapshotLoadedPlugins();
                foreach (var i in plugins)
                    ctx.WriteLine($"{i.Name} v{i.Version}  by {i.Author}");
                if (plugins.Count == 0)
                    ctx.WriteLine("（没有加载任何插件）", ConsoleColor.DarkGray);
                return Task.CompletedTask;
            });

            await RunLiveDisplayApplicationAsync(uiHost);
        }

        static Task StartPluginInitializationAsync(BootstrapWorkspace bootstrap)
        {
            return Task.Run(() =>
            {
                bootstrap.SetPhase("plugin-scan", "插件扫描", LiveDisplaySeverity.Info, "正在扫描 Plugins/。");
                try
                {
                    PluginManager.Init();
                }
                catch (Exception ex)
                {
                    bootstrap.SetPhase("plugin-scan", "插件扫描", LiveDisplaySeverity.Error, ex.Message);
                    LiveDisplayConsole.LogException("Plugin", ex);
                    throw;
                }

                var loadedPluginCount = PluginManager.LoadedPlugins.Count;
                var failedPluginCount = PluginManager.FailedPlugins.Count;
                bootstrap.SetPhase(
                    "plugin-scan",
                    "插件扫描",
                    failedPluginCount == 0 ? LiveDisplaySeverity.Success : LiveDisplaySeverity.Warning,
                    failedPluginCount == 0
                        ? $"发现 {loadedPluginCount} 个可用插件。"
                        : $"发现 {loadedPluginCount} 个可用插件，{failedPluginCount} 个插件失败。");
            });
        }

        static IReadOnlyList<(string Label, string Value)> BuildBootstrapSettings()
        {
            return
            [
                ("版本", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"),
                ("工作目录", Directory.GetCurrentDirectory()),
                ("监听", $"http://{Config.Core.ListenAddress}:{Config.Core.ListenPort}"),
                ("服务器目标", Config.Repository.Targets.Count == 0 ? "未限制" : string.Join(", ", Config.Repository.Targets)),
                ("数据语言", Config.Updater.DatabaseLanguage),
                ("训练员性别", Config.Updater.TrainerIsMale ? "男" : "女")
            ];
        }

        static async Task CheckPluginUpdatesAsync(UiHost uiHost)
        {
            try
            {
                var updates = await PluginRepository.CheckForUpdatesAsync();
                if (updates.Count == 0)
                    return;

                uiHost.Notify(new LiveDisplayNotification(
                    Workspace: null,
                    "URA",
                    FormatPluginUpdateNotification(updates),
                    LiveDisplaySeverity.Info,
                    DateTimeOffset.Now.AddSeconds(12)));

                foreach (var update in updates)
                {
                    uiHost.Log(new LiveDisplayLogLine(
                        Workspace: null,
                        "URA",
                        $"插件 {update.DisplayName} 有新版本可用: {update.CurrentVersion} -> {update.LatestVersion}",
                        LiveDisplaySeverity.Info,
                        IsMarkup: false,
                        DateTimeOffset.Now));
                }
            }
            catch (Exception ex)
            {
                uiHost.Notify(new LiveDisplayNotification(
                    Workspace: null,
                    "URA",
                    $"插件更新检查失败: {ex.Message}",
                    LiveDisplaySeverity.Warning,
                    LiveDisplayNotification.ExpiresAtFromNow(LiveDisplaySeverity.Warning)));
            }
        }

        static string FormatPluginUpdateNotification(IReadOnlyList<PluginUpdateInfo> updates)
        {
            if (updates.Count == 1)
            {
                var update = updates[0];
                return $"插件 {update.DisplayName} 有新版本: {update.CurrentVersion} -> {update.LatestVersion}";
            }

            var names = string.Join("、", updates.Take(3).Select(x => x.DisplayName));
            var more = updates.Count > 3 ? " 等" : string.Empty;
            return $"{updates.Count} 个插件可更新：{names}{more}。到「插件仓库」菜单里手动安装。";
        }

        static async Task RunLiveDisplayApplicationAsync(UiHost uiHost)
        {
            using var cts = new CancellationTokenSource();
            var uiTask = uiHost.RunAsync(cts.Token);
            var keyboardTask = KeyboardManager.RunAsync(cts.Token);

            try
            {
                await Task.WhenAny(uiTask, keyboardTask);
            }
            finally
            {
                cts.Cancel();
                KeyboardManager.Stop();
                uiHost.RequestShutdown();
                KeyboardManager.OverlaySink = null;
                LiveDisplayConsole.Unbind(uiHost);
            }

            try
            {
                await Task.WhenAll(uiTask, keyboardTask);
            }
            catch (OperationCanceledException) when (!uiTask.IsFaulted && !keyboardTask.IsFaulted)
            {
            }

            ThrowIfFaulted(uiTask);
            ThrowIfFaulted(keyboardTask);
        }

        static void ThrowIfFaulted(Task task)
        {
            if (task.Exception?.InnerExceptions.FirstOrDefault(x => x is not OperationCanceledException) is { } exception)
                ExceptionDispatchInfo.Capture(exception).Throw();
        }

        static void ShowFirstLaunchPrompt()
        {
            LiveDisplayConsole.WriteLine("检测到是第一次运行，将进行一些初始设置。使用方向键↑与↓切换选项，使用回车[Enter]确认。");
            LiveDisplayConsole.WriteLine("推荐使用Windows终端(Windows Terminal)，并将启动大小设置为120列35行以获得更好的体验。");
            LiveDisplayConsole.WriteLine();

            var mobileOrPc = LiveDisplayConsole.Prompt(
                new SelectionPrompt<string>()
                .Title("请选择运行UM:PD的设备")
                .AddChoices(["手机/模拟器以及此计算机", "此计算机"]));
            if (mobileOrPc == "手机/模拟器以及此计算机")
            {
                Config.Core.ListenAddress = "0.0.0.0";
                LiveDisplayConsole.WriteLine("已配置URA为接受来自其他设备的请求。在初次启动时可能会提示防火墙放行，请务必同意，否则可能无法正常连接。如果因某种原因没有同意，请自行搜索如何在Windows中放行应用程序");
            }
            else
            {
                LiveDisplayConsole.WriteLine("已配置URA为仅接受来自此计算机的请求。如需要使模拟器连入，需从选项->核心中将监听地址更改为0.0.0.0并在启动时放行防火墙。");
            }
            LiveDisplayConsole.WriteLine();

            var targets = LiveDisplayConsole.Prompt(
                new MultiSelectionPrompt<string>()
                .Title("请选择你所使用的UM:PD版本，只选择繁中服的话就不会显示未来才能使用的插件防止报错。")
                .AddChoices(["日服(Cygames)", "繁中服(Komoe)"]));
            foreach (var target in targets)
            {
                switch (target)
                {
                    case "日服(Cygames)":
                        Config.Repository.Targets.Add("Cygames");
                        break;
                    case "繁中服(Komoe)":
                        Config.Repository.Targets.Add("Komoe");
                        break;
                }
            }

            var dbLang = LiveDisplayConsole.Prompt(
                new SelectionPrompt<string>()
                .Title("请选择事件数据语言，选择繁中等将会使用对应客户端已实装的内容翻译。不会影响实际效果及数据库总大小。")
                .AddChoices(["日文", "繁中"]));
            Config.Updater.DatabaseLanguage = dbLang == "繁中" ? "zh-TW" : "ja-JP";

            var trainerGender = LiveDisplayConsole.Prompt(
                new SelectionPrompt<string>()
                .Title("请选择训练员性别，用于精确显示事件选项。")
                .AddChoices(["男", "女"]));
            Config.Updater.TrainerIsMale = trainerGender == "男";

            LiveDisplayConsole.MarkupLine("在正式开始使用之前，请先[green]更新数据文件[/]并根据需求[green]前往[[插件仓库]]安装自己需要的插件[/]。");
            LiveDisplayConsole.MarkupLine("否则URA将[red]没有任何功能[/]。");
        }
        static async Task<string> ShowMenu()
        {
            var selections = new SelectionPrompt<string>()
                .Title(I18N_Instruction)
                .WrapAround(true)
                .AddChoices(
                [
                    I18N_Start,
                    I18N_Options,
                    "插件仓库",
                    I18N_UpdateAssets,
                    I18N_UpdateProgram,
                    "加入QQ群（号被封过之后在频道里说话会概率被夹"
                ]
                );
            #region 条件显示功能
            // Windows限定功能，其他平台不显示
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                selections.AddChoice(I18N_InstallUraCore);
            }
            #endregion
            var prompt = LiveDisplayConsole.Prompt(selections);
            if (prompt == I18N_Options)
            {
                Config.Prompt();
            }
            else if (prompt == "插件仓库")
            {
                await PluginRepository.ShowMenuAsync();
            }
            else if (prompt == I18N_UpdateAssets)
            {
                await ResourceUpdater.UpdateAssets();
            }
            else if (prompt == I18N_UpdateProgram)
            {
                await ResourceUpdater.UpdateProgram();
            }
            else if (prompt == I18N_InstallUraCore && UraCoreHelper.GamePaths.Count != 0)
            {
                LiveDisplayConsole.Clear();
                var target = LiveDisplayConsole.Prompt(new TextPrompt<string>("请选择想要安装的Mod: ").AddChoices(["umamusume-localify", "Hachimi"]).DefaultValue("Hachimi"));
                LiveDisplayConsole.WriteLine(I18N_UraCoreHelper_FoundPaths, UraCoreHelper.GamePaths.Count);

                foreach (var i in UraCoreHelper.GamePaths)
                {
                    LiveDisplayConsole.WriteLine(I18N_UraCoreHelper_FoundAvailablePath, i);
                    var confirm = LiveDisplayConsole.Prompt(new ConfirmationPrompt($"是否需要将{target}安装到{i}？该操作需要管理员权限。"));
                    if (confirm)
                    {
                        using var proc = new Process();
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = Environment.ProcessPath,
                            Arguments = "--enable-dll-redirection",
                            CreateNoWindow = true,
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        proc.StartInfo = startInfo;
                        proc.Start();
                        proc.WaitForExit();

                        if (proc.ExitCode == 0)
                        {
                            var url = (target == "Hachimi"
                                ? "https://github.com/UmamusumeResponseAnalyzer/Hachimi/releases/latest/download/Hachimi.zip"
                                : "https://github.com/UmamusumeResponseAnalyzer/Hachimi/releases/latest/download/UmamusumeLocalify.zip")
                                .AllowMirror();
                            using var stream = await ResourceUpdater.HttpClient.GetStreamAsync(url);
                            using var archive = new ZipArchive(stream);
                            archive.ExtractToDirectory(i, true);

                            LiveDisplayConsole.WriteLine(I18N_UraCoreHelper_InstallSuccess, i);
                        }
                    }
                }
                LiveDisplayConsole.ReadKey();
            }
            else if (prompt == "加入QQ群（号被封过之后在频道里说话会概率被夹")
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://qm.qq.com/q/4z6xHQ908w",
                    UseShellExecute = true
                });
                LiveDisplayConsole.WriteLine("https://qm.qq.com/q/4z6xHQ908w");
                LiveDisplayConsole.WriteLine("按任意键返回到主菜单...");
                LiveDisplayConsole.ReadKey();
            }
            LiveDisplayConsole.Clear();

            return prompt;
        }
        static async Task ParseArguments(string[] args)
        {
            switch (args.Length)
            {
                case 0:
                    await ResourceUpdater.TryUpdateProgram();
                    return;
                case 1:
                    {
                        switch (args[0])
                        {
                            case "-v" or "--version":
                                {
                                    Console.Write(Assembly.GetExecutingAssembly().GetName().Version);
                                    Environment.Exit(0);
                                    return;
                                }
                            case "--enable-dll-redirection":
                                {
                                    UraCoreHelper.EnableDllRedirection();
                                    _ = Task.Run(async () =>
                                    {
                                        await Task.Delay(3000);
                                        Environment.Exit(Environment.ExitCode);
                                    });
                                    Console.ReadLine();
                                    Environment.Exit(Environment.ExitCode);
                                    break;
                                }
                        }
                        return;
                    }
                case 2:
                    {
                        switch (args[0])
                        {
                            case "--update":
                                {
                                    await ResourceUpdater.TryUpdateProgram(args[1]);
                                    return;
                                }
                            case "--update-data":
                                {
                                    ZipFile.ExtractToDirectory(args[1], "./");
                                    return;
                                }
                        }
                        return;
                    }
            }
        }
        internal static void ApplyCultureInfo()
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(LanguageConfig.GetCulture());
            Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(LanguageConfig.GetCulture());
            foreach (var i in Assembly.GetExecutingAssembly().GetTypes().Where(x => x.IsClass && x.Namespace?.StartsWith("UmamusumeResponseAnalyzer.Localization") == true))
            {
                var rc = i?.GetField("resourceCulture", BindingFlags.NonPublic | BindingFlags.Static);
                if (rc == null) continue;
                rc.SetValue(null, Thread.CurrentThread.CurrentUICulture);
            }
        }
        internal static void Restart()
        {
            var exePath = Environment.ProcessPath!;
            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)!
            });
            Environment.Exit(0);
        }
    }
}
