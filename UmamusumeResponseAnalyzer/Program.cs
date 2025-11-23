using Newtonsoft.Json;
using Spectre.Console;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Plugin;
using static UmamusumeResponseAnalyzer.Localization.DMM;
using static UmamusumeResponseAnalyzer.Localization.LaunchMenu;
using static UmamusumeResponseAnalyzer.Localization.NetFilter;

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
            var handlerRoutine = new ConsoleHelper.HandlerRoutine(ConsoleHelper.ConsoleCtrlCheck);
            GC.KeepAlive(handlerRoutine);
            ConsoleHelper.SetConsoleCtrlHandler(handlerRoutine, true);
            ConsoleHelper.DisableQuickEditMode();
            Console.Title = $"UmamusumeResponseAnalyzer v{Assembly.GetExecutingAssembly().GetName().Version}";
            Console.OutputEncoding = Encoding.UTF8;
            Environment.SetEnvironmentVariable("DOTNET_SYSTEM_NET_DISABLEIPV6", "true");
            if (!Directory.Exists(WORKING_DIRECTORY)) Directory.CreateDirectory(WORKING_DIRECTORY);
            Directory.SetCurrentDirectory(WORKING_DIRECTORY);
            await ParseArguments(args);

            Config.Initialize();
            if (Config.Core.ShowFirstRunPrompt)
            {
                ShowFirstLaunchPrompt();
                Config.Core.ShowFirstRunPrompt = false;
                Config.Save();
            }

            _plugin_initialize_task = Task.Run(PluginManager.Init);
            var prompt = string.Empty;
            do
            {
                prompt = await ShowMenu();
            }
            while (prompt != I18N_Start); //如果不是启动则重新显示主菜单

            _database_initialize_task = Database.Initialize();
            Task.WaitAll(_database_initialize_task, _plugin_initialize_task); //等待数据库初始化完成
            Server.Start(); //启动HTTP服务器
            Task.WaitAll([.. PluginManager.LoadedPlugins.Select(x => Task.Run(x.Initialize))]);

            if (Config.NetFilter.Enable)
                await NetFilter.Enable();
            //如果存在DMM的token文件则启用直接登录功能
            if (Config.DMM.Enable && Config.DMM.Accounts.Count != 0)
            {
                if (Config.DMM.Accounts.Count == 1)
                {
                    await DMM.RunUmamusume(Config.DMM.Accounts[0]);
                }
                else
                {
                    prompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
                        .Title(I18N_MultipleAccountsFound)
                        .WrapAround(true)
                        .AddChoices(Config.DMM.Accounts.Select(x => x.Name))
                        .AddChoices([I18N_LaunchAll, I18N_Cancel]));
                    if (prompt == I18N_LaunchAll)
                    {
                        DMM.IgnoreExistProcess = true;
                        foreach (var account in Config.DMM.Accounts)
                            await DMM.RunUmamusume(account);
                    }
                    else if (prompt == I18N_Cancel)
                    {
                    }
                    else
                    {
                        var account = Config.DMM.Accounts.Find(x => x.Name == prompt);
                        if (account != default)
                        {
                            await DMM.RunUmamusume(account);
                        }
                    }
                }
            }

            for (var i = 0; i < 30; i++)
            {
                if (Server.IsRunning) break;
                await Task.Delay(100);
            }
            if (!Server.IsRunning)
            {
                AnsiConsole.WriteLine(I18N_LaunchFail);
                Console.ReadLine();
                Environment.Exit(1);
            }

            AnsiConsole.MarkupLine(I18N_Start_Started);
            var _closingEvent = new AutoResetEvent(false);
            Console.CancelKeyPress += (_, _) =>
            {
                Server.Stop();
                foreach (var plugin in PluginManager.LoadedPlugins)
                {
                    plugin.Dispose();
                }
                ResourceUpdater.HttpClient.Dispose();
                _closingEvent.Set();
            };
            _closingEvent.WaitOne();
        }
        static void ShowFirstLaunchPrompt()
        {
            AnsiConsole.WriteLine("检测到是第一次运行，将进行一些初始设置。使用方向键↑与↓切换选项，使用回车[Enter]确认。");
            AnsiConsole.WriteLine("推荐使用Windows终端(Windows Terminal)，并将启动大小设置为120列35行以获得更好的体验。");
            AnsiConsole.WriteLine();

            var mobileOrPc = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                .Title("请选择运行UM:PD的设备")
                .AddChoices(["手机/模拟器以及此计算机", "此计算机"]));
            if (mobileOrPc == "手机/模拟器以及此计算机")
            {
                Config.Core.ListenAddress = "0.0.0.0";
                AnsiConsole.WriteLine("已配置URA为接受来自其他设备的请求。在初次启动时可能会提示防火墙放行，请务必同意，否则可能无法正常连接。如果因某种原因没有同意，请自行搜索如何在Windows中放行应用程序");
            }
            else
            {
                AnsiConsole.WriteLine("已配置URA为仅接受来自此计算机的请求。如需要使模拟器连入，需从选项->核心中将监听地址更改为0.0.0.0并在启动时放行防火墙。");
            }
            AnsiConsole.WriteLine();

            var targets = AnsiConsole.Prompt(
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

            var dbLang = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                .Title("请选择事件数据语言，选择繁中等将会使用对应客户端已实装的内容翻译。不会影响实际效果及数据库总大小。")
                .AddChoices(["日文", "繁中"]));
            Config.Updater.DatabaseLanguage = dbLang == "繁中" ? "zh-TW" : "ja-JP";

            var trainerGender = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                .Title("请选择训练员性别，用于精确显示事件选项。")
                .AddChoices(["男", "女"]));
            Config.Updater.TrainerIsMale = trainerGender == "男";

            AnsiConsole.MarkupLine("在正式开始使用之前，请先[green]更新数据文件[/]并根据需求[green]前往[[插件仓库]]安装自己需要的插件[/]。");
            AnsiConsole.MarkupLine("否则URA将[red]没有任何功能[/]。");
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
                if (Config.NetFilter.Enable && WORKING_DIRECTORY != PORTABLE_WORKING_DIRECTORY)
                {
                    if (File.Exists($"{Environment.SystemDirectory}\\drivers\\netfilter2.sys"))
                    {
                        selections.AddChoice(I18N_ConfigProxyServer);
                        selections.AddChoice(I18N_NFDriver_Uninstall);
                    }
                    else
                    {
                        selections.AddChoice(I18N_NFDriver_Install);
                    }
                }
                selections.AddChoice(I18N_InstallUraCore);
            }
            #endregion
            var prompt = AnsiConsole.Prompt(selections);
            if (prompt == I18N_Options)
            {
                Config.Prompt();
            }
            else if (prompt == "插件仓库")
            {
                var defaultRepository = "https://raw.githubusercontent.com/URA-Plugins/PluginMaster/refs/heads/master/PluginMaster/manifests.json".AllowMirror();
                var repositories = new Dictionary<string, string>
                {
                    ["默认仓库"] = defaultRepository
                };
                foreach (var (repositoryName, repository) in Config.Repository.AdditionalPluginRepositories)
                {
                    repositories.Add(repositoryName, repository);
                }

                var plugins = new Dictionary<string, (string Description, PluginInformation PluginInfo)>();
                foreach (var (repositoryName, repository) in repositories)
                {
                    AnsiConsole.WriteLine($"正在从{repositoryName}获取插件信息");
                    var jsonText = await ResourceUpdater.HttpClient.GetStringAsync(repository);
                    var jsonObj = JsonConvert.DeserializeObject<List<PluginInformation>>(jsonText);
                    if (jsonObj != default)
                    {
                        foreach (var plugin in jsonObj.Where(x => x.Targets.Length == 0 || x.Targets.Intersect(Config.Repository.Targets).Any()))
                        {
                            if (repositoryName == "默认仓库" && Uri.TryCreate(plugin.DownloadUrl, UriKind.Absolute, out var uri) && uri.Host.EndsWith("github.com"))
                            {
                                plugin.DownloadUrl = plugin.DownloadUrl.AllowMirror();
                            }
                            if (plugins.ContainsKey(plugin.Name))
                            {
                                plugins.Add($"{plugin.Name}({repositoryName})", (plugin.Description, plugin));
                            }
                            plugins.Add(plugin.Name, (plugin.Description, plugin));
                        }
                    }
                }
                AnsiConsole.Clear();

                var pluginSelection = new MultiSelectionPrompt<string>()
                    .Title("选择要安装的插件")
                    .WrapAround(true)
                    .AddChoices(plugins.Select(x => $"{x.Key}: {x.Value.Description}"))
                    .PageSize(30)
                    .NotRequired();
                var selectedPlugins = AnsiConsole.Prompt(pluginSelection)
                    .Select(x => plugins[x.Split(':')[0]].PluginInfo)
                    .ToList();
                if (selectedPlugins.Count == 0)
                {
                    AnsiConsole.Clear();
                    return prompt;
                }

#warning TODO: 如果依赖套依赖，需要再处理，偷个懒先
                var dependencies = selectedPlugins.SelectMany(x => x.Dependencies).Distinct().ToArray();
                foreach (var dependency in dependencies)
                {
                    if (!selectedPlugins.Any(x => x.InternalName == dependency))
                    {
                        var dependencyPluginInfo = plugins.FirstOrDefault(x => x.Value.PluginInfo.InternalName == dependency);
                        if (dependencyPluginInfo.Key == default || dependencyPluginInfo.Value == default)
                        {
                            AnsiConsole.WriteLine($"没有在任何插件仓库中找到依赖项{dependency}");
                        }
                        else
                        {
                            selectedPlugins.Add(dependencyPluginInfo.Value.PluginInfo);
                        }
                    }
                }

                foreach (var plugin in selectedPlugins)
                {
                    AnsiConsole.WriteLine($"[{plugin.Name}] 正在下载");
                    using var stream = await ResourceUpdater.HttpClient.GetStreamAsync(plugin.DownloadUrl);
                    using var archive = new ZipArchive(stream);
                    AnsiConsole.WriteLine($"[{plugin.Name}] 正在解压");
                    archive.ExtractToDirectory("./", true);
                    AnsiConsole.WriteLine($"[{plugin.Name}] 安装完成");
                }

                if (selectedPlugins.Count > 0)
                {
                    AnsiConsole.WriteLine("插件安装已全部完成，按任意键重新启动。");
                    Console.ReadKey();
                    Restart();
                }
                Console.ReadKey();
            }
            else if (prompt == I18N_UpdateAssets)
            {
                await ResourceUpdater.UpdateAssets();
            }
            else if (prompt == I18N_UpdateProgram)
            {
                await ResourceUpdater.UpdateProgram();
            }
            else if (prompt == I18N_NFDriver_Install)
            {
                await ResourceUpdater.DownloadNetFilter(NetFilter.nfapiPath, NetFilter.nfdriverPath, NetFilter.redirectorPath);
                using var Proc = new Process();
                var StartInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    Arguments = "--install-netfilter-driver",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Proc.StartInfo = StartInfo;
                Proc.Start();
                Proc.WaitForExit();
                if (File.Exists(NetFilter.nfapiPath) && File.Exists(NetFilter.nfdriverPath) && File.Exists(NetFilter.redirectorPath) && File.Exists($"{Environment.SystemDirectory}\\drivers\\netfilter2.sys"))
                {
                    Config.NetFilter.Enable = true;
                    Config.Save();
                }
            }
            else if (prompt == I18N_NFDriver_Uninstall)
            {
                using var Proc = new Process();
                var StartInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    Arguments = "--uninstall-netfilter-driver",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Proc.StartInfo = StartInfo;
                Proc.Start();
                Proc.WaitForExit();
                Config.NetFilter.Enable = false;
                Config.Save();
            }
            else if (prompt == I18N_InstallUraCore && UraCoreHelper.GamePaths.Count != 0)
            {
                AnsiConsole.Clear();
                var target = AnsiConsole.Prompt(new TextPrompt<string>("请选择想要安装的Mod: ").AddChoices(["umamusume-localify", "Hachimi"]).DefaultValue("Hachimi"));
                AnsiConsole.WriteLine(I18N_UraCoreHelper_FoundPaths, UraCoreHelper.GamePaths.Count);

                foreach (var i in UraCoreHelper.GamePaths)
                {
                    AnsiConsole.WriteLine(I18N_UraCoreHelper_FoundAvailablePath, i);
                    var confirm = AnsiConsole.Prompt(new ConfirmationPrompt($"是否需要将{target}安装到{i}？该操作需要管理员权限。"));
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

                            AnsiConsole.WriteLine(I18N_UraCoreHelper_InstallSuccess, i);
                        }
                    }
                }
                Console.ReadKey();
            }
            else if (prompt == "加入QQ群（号被封过之后在频道里说话会概率被夹")
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://qm.qq.com/q/4z6xHQ908w",
                    UseShellExecute = true
                });
                AnsiConsole.WriteLine("https://qm.qq.com/q/4z6xHQ908w");
                AnsiConsole.WriteLine("按任意键返回到主菜单...");
                Console.ReadKey();
            }
            AnsiConsole.Clear();

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
                            case "--install-netfilter-driver":
                                {
                                    AnsiConsole.Status().Start(I18N_NFDriver_InstallingAlert, (ctx) =>
                                    {
                                        ctx.Spinner(Spinner.Known.BouncingBar);
                                        NetFilter.InstallDriver();
                                    });
                                    Environment.Exit(0);
                                    return;
                                }
                            case "--uninstall-netfilter-driver":
                                {
                                    AnsiConsole.Status().Start(I18N_NFDriver_UninstallingAlert, (ctx) =>
                                    {
                                        ctx.Spinner(Spinner.Known.BouncingBar);
                                        NetFilter.UninstallDriver();
                                    });
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
        internal static void ApplyCultureInfo(LanguageConfig.Language culture)
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(LanguageConfig.GetCulture());
            Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(LanguageConfig.GetCulture());
            foreach (var i in Assembly.GetExecutingAssembly().GetTypes().Where(x => x.IsClass && x.Namespace?.StartsWith("UmamusumeResponseAnalyzer.Localization") == true))
            {
                var rc = i?.GetField("resourceCulture", BindingFlags.NonPublic | BindingFlags.Static);
                if (rc == null) continue;
                rc.SetValue(rc, Thread.CurrentThread.CurrentUICulture);
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

    internal static partial class ConsoleHelper
    {
        /// <summary>
        ///     Adapted from
        ///     http://stackoverflow.com/questions/13656846/how-to-programmatic-disable-c-sharp-console-applications-quick-edit-mode
        /// </summary>
        #region Disable Quick Edit Mode
        private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
        private const uint ENABLE_MOUSE_INPUT = 0x0010;

        // STD_INPUT_HANDLE (DWORD): -10 is the standard input device.
        private const int StdInputHandle = -10;

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr GetStdHandle(int nStdHandle);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        internal static bool DisableQuickEditMode()
        {
            var consoleHandle = GetStdHandle(StdInputHandle);

            // get current console mode
            if (!GetConsoleMode(consoleHandle, out var consoleMode))
                // ERROR: Unable to get console mode.
                return false;

            // Clear the quick edit bit in the mode flags
            consoleMode &= ~ENABLE_QUICK_EDIT_MODE;
            consoleMode &= ~ENABLE_MOUSE_INPUT;

            // set the new mode
            if (!SetConsoleMode(consoleHandle, consoleMode))
                // ERROR: Unable to set console mode
                return false;

            return true;
        }
        #endregion

        /// <summary>
        /// https://danielkaes.wordpress.com/2009/06/30/how-to-catch-%E2%80%9Ckill%E2%80%9D-events-in-a-c-console-application/
        /// </summary>
        #region Dispose When Click X
        /// <summary>
        /// This function sets the handler for kill events.
        /// </summary>
        /// <param name="Handler"></param>
        /// <param name="Add"></param>
        /// <returns></returns>
        [LibraryImport("Kernel32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetConsoleCtrlHandler(HandlerRoutine Handler, [MarshalAs(UnmanagedType.Bool)] bool Add);

        //delegate type to be used of the handler routine
        internal delegate bool HandlerRoutine(CtrlTypes CtrlType);

        // control messages
        internal enum CtrlTypes
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT,
            CTRL_CLOSE_EVENT,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT
        }
        internal static bool ConsoleCtrlCheck(CtrlTypes ctrlType)
        {
            Server.Stop();
            foreach (var plugin in PluginManager.LoadedPlugins)
            {
                plugin.Dispose();
            }
            ResourceUpdater.HttpClient.Dispose();
            return true;
        }
        #endregion
    }
}