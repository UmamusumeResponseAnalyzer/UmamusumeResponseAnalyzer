using Spectre.Console;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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
        public static async Task Main(string[] args)
        {
            var handlerRoutine = new ConsoleHelper.HandlerRoutine(ConsoleHelper.ConsoleCtrlCheck);
            GC.KeepAlive(handlerRoutine);
            ConsoleHelper.SetConsoleCtrlHandler(handlerRoutine, true);
            ConsoleHelper.DisableQuickEditMode();
            Console.Title = $"UmamusumeResponseAnalyzer v{Assembly.GetExecutingAssembly().GetName().Version}";
            Console.OutputEncoding = Encoding.UTF8;
            Environment.SetEnvironmentVariable("DOTNET_SYSTEM_NET_DISABLEIPV6", "true");
            Directory.SetCurrentDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer"));
            await ParseArguments(args);

            Config.Initialize();
            _database_initialize_task = Database.Initialize();
            _plugin_initialize_task = Task.Run(PluginManager.Init);

            var prompt = string.Empty;
            do
            {
                prompt = await ShowMenu();
            }
            while (prompt != I18N_Start); //如果不是启动则重新显示主菜单

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
                _closingEvent.Set();
            };
            _closingEvent.WaitOne();
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
                    I18N_UpdateAssets,
                    I18N_UpdateProgram
                ]
                );
            #region 条件显示功能
            // Windows限定功能，其他平台不显示
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (Config.NetFilter.Enable)
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
                var applicationDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer");
                var nfapiPath = Path.Combine(applicationDir, "nfapi.dll");
                var nfdriverPath = Path.Combine(applicationDir, "nfdriver.sys");
                var redirectorPath = Path.Combine(applicationDir, "Redirector.dll");
                await ResourceUpdater.DownloadNetFilter(nfapiPath, nfdriverPath, redirectorPath);
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
                if (File.Exists(nfapiPath) && File.Exists(nfdriverPath) && File.Exists(redirectorPath) && File.Exists($"{Environment.SystemDirectory}\\drivers\\netfilter2.sys"))
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
                    var confirm = AnsiConsole.Prompt(new ConfirmationPrompt($"是否需要将{target}安装到{0}？该操作需要管理员权限。"));
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
                            var url = target == "Hachimi"
                                ? "https://assets.shuise.net/URA/Hachimi.zip"
                                : "https://assets.shuise.net/URA/UmamusumeLocalify.zip";
                            var client = new HttpClient();
                            using var stream = await client.GetStreamAsync(url);
                            using var archive = new ZipArchive(stream);
                            archive.ExtractToDirectory(i, true);

                            AnsiConsole.WriteLine(I18N_UraCoreHelper_InstallSuccess, i);
                        }
                    }
                }
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
                                    ZipFile.ExtractToDirectory(args[1], Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer"));
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
        [DllImport("Kernel32")]
        internal static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);

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
            return true;
        }
        #endregion
    }
}