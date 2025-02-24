using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.LocalizedLayout;
using static UmamusumeResponseAnalyzer.Localization.DMM;
using static UmamusumeResponseAnalyzer.Localization.LaunchMenu;
using static UmamusumeResponseAnalyzer.Localization.NetFilter;

namespace UmamusumeResponseAnalyzer
{
    public static class UmamusumeResponseAnalyzer
    {
        static bool runInCmder = false;
        static System.Globalization.CultureInfo defaultUICulture = null!;
        public static async Task Main(string[] args)
        {
            defaultUICulture = System.Globalization.CultureInfo.CurrentUICulture;
            ConsoleHelper.DisableQuickEditMode();
            Console.Title = $"UmamusumeResponseAnalyzer v{Assembly.GetExecutingAssembly().GetName().Version}";
            Console.OutputEncoding = Encoding.UTF8;
            Environment.SetEnvironmentVariable("DOTNET_SYSTEM_NET_DISABLEIPV6", "true");
            //加载设置
            Config.Initialize();
            await ParseArguments(args);

            if (!AnsiConsole.Profile.Capabilities.Ansi && !runInCmder)
            { //不支持ANSI Escape Sequences，用Cmder打开
                var cmderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "cmder");
                if (!Directory.Exists(cmderPath))
                    await ResourceUpdater.DownloadCmder(cmderPath);
                using var Proc = new Process();
                var StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(cmderPath, "Cmder.exe"),
                    Arguments = $"/start \"{Environment.CurrentDirectory}\" /task URA",
                    CreateNoWindow = false,
                    UseShellExecute = true
                };
                Proc.StartInfo = StartInfo;
                Proc.Start();
                Proc.WaitForExit();
                Environment.Exit(0);
            }

            if (AnsiConsole.Console.Profile.Width < RecommendTerminalSize.Current.Width ||
                AnsiConsole.Console.Profile.Height < RecommendTerminalSize.Current.Height)
            {
                AnsiConsole.WriteLine(I18N_ConsoleSizeSmall, RecommendTerminalSize.Current.Width, RecommendTerminalSize.Current.Height);
            }
            var prompt = string.Empty;
            do
            {
                prompt = await ShowMenu();
            }
            while (prompt != I18N_Start); //如果不是启动则重新显示主菜单

            Database.Initialize(); //初始化马娘相关数据
            Server.Start(); //启动HTTP服务器

            if (Config.Get(Localization.Config.I18N_EnableNetFilter))
                await NetFilter.Enable();
            //如果存在DMM的token文件则启用直接登录功能
            if (File.Exists(DMM.DMM_CONFIG_FILEPATH) && Config.Get(Localization.Config.I18N_DMMLaunch) && DMM.Accounts.Count != 0)
            {
                if (DMM.Accounts.Count == 1)
                {
                    DMM.Accounts[0].RunUmamusume();
                }
                else
                {
                    prompt = AnsiConsole.Prompt(new SelectionPrompt<string>()
                        .Title(I18N_MultipleAccountsFound)
                        .AddChoices(DMM.Accounts.Select(x => x.Name))
                        .AddChoices([I18N_LaunchAll, I18N_Cancel]));
                    if (prompt == I18N_LaunchAll)
                    {
                        DMM.IgnoreExistProcess = true;
                        foreach (var account in DMM.Accounts)
                            account.RunUmamusume();
                    }
                    else if (prompt == I18N_Cancel)
                    {
                    }
                    else
                    {
                        DMM.Accounts.Find(x => x.Name == prompt)?.RunUmamusume();
                    }
                }
            }

            if (!Server.IsRunning)
            {
                AnsiConsole.WriteLine(I18N_LaunchFail);
                Console.ReadLine();
            }
            else
            {
                AnsiConsole.MarkupLine(I18N_Start_Started);
                var _closingEvent = new AutoResetEvent(false);
                Console.CancelKeyPress += ((s, a) =>
                {
                    _closingEvent.Set();
                });
                _closingEvent.WaitOne();
            }
        }
        static async Task<string> ShowMenu()
        {
            var selections = new SelectionPrompt<string>()
                .Title(I18N_Instruction)
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
                if (Config.Get(Localization.Config.I18N_EnableNetFilter))
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
                if (UraCoreHelper.GamePaths.Count != 0)
                {
                    selections.AddChoice(I18N_InstallUraCore);
                }
            }
            // 仅在启用本地化设置时显示相关设置
            if (Config.Get(Localization.Config.I18N_LoadLocalizedData) && (string.IsNullOrEmpty(Config.Get<string>(Localization.Config.I18N_LocalizedDataPath)) || (!string.IsNullOrEmpty(Config.Get<string>(Localization.Config.I18N_LocalizedDataPath)) && !File.Exists(Config.Get<string>(Localization.Config.I18N_LocalizedDataPath)))))
            {
                selections.AddChoice(I18N_SetLocalizedDataFilePath);
            }
            // 仅在启用跳过DMM启动时显示相关设置
            if (Config.Get(Localization.Config.I18N_DMMLaunch))
            {
                selections.AddChoice(I18N_ManageDMMService);
            }
            #endregion
            var prompt = AnsiConsole.Prompt(selections);
            if (prompt == I18N_Options)
            {
                var multiSelection = new MultiSelectionPrompt<string>()
                    .Title(I18N_Options)
                    .Mode(SelectionMode.Leaf)
                    .PageSize(25)
                    .InstructionsText(I18N_Options_Instruction);

                // 根据预设值添加选项
                foreach (var i in Config.ConfigSet)
                {
                    multiSelection.AddChoiceGroup(i.Key, i.Value.Where(x => x.Visiable).Select(x => x.Key));
                }
                // 复原配置文件的选择情况
                foreach (var i in Config.Configuration)
                {
                    if (i.Value.Value.GetType() == typeof(bool) && (bool)i.Value.Value)
                    {
                        multiSelection.Select(i.Key);
                    }
                }
                // 如果配置文件中的某一组全被选中，则也选中对应的组
                foreach (var i in Config.ConfigSet)
                {
                    var visiableItems = Config.Configuration.Where(x => x.Value.Visiable);
                    var visiableConfig = i.Value.Where(x => x.Visiable);
                    if (i.Value.Any() && visiableItems.Where(x => x.Value.Value.GetType() == typeof(bool) && (bool)x.Value.Value).Select(x => x.Key).Intersect(i.Value.Select(x => x.Key)).Count() == visiableConfig.Count())
                    {
                        multiSelection.Select(i.Key);
                    }
                }

                // 进入设置前先保存之前的语言设置
                var previousLanguage = string.Empty;
                var availableLanguages = Config.ConfigSet.First(x => Config.LanguageSectionKeys.Contains(x.Key)).Value.Select(x => x.Key);
                var languages = availableLanguages.Select(x => Config.Configuration[x]);
                var languagesEnabled = languages.Where(x => Config.Get(x.Key));
                previousLanguage = languagesEnabled.First().Key;

                var options = AnsiConsole.Prompt(multiSelection);
                foreach (var i in Config.Configuration.Keys)
                {
                    if (Config.Get<object>(i)?.GetType() != typeof(bool))
                        continue;
                    Config.Set(i, options.Contains(i));
                }

                languages = availableLanguages.Select(x => Config.Configuration[x]);
                languagesEnabled = languages.Where(x => Config.Get(x.Key));
                var selectedLanguage = languagesEnabled.First();
                if (selectedLanguage == default)
                {
                    Config.Set(Localization.Config.I18N_Language_AutoDetect, true);
                    Config.SaveConfigForLanguageChange();
                    ApplyCultureInfo(Localization.Config.I18N_Language_AutoDetect);
                    Config.LoadConfigForLanguageChange();
                }
                else if (languagesEnabled.Count() > 1)
                {
                    foreach (var i in languages)
                    {
                        Config.Set(i.Key, false);
                    }
                    Config.Set(Localization.Config.I18N_Language_AutoDetect, true);
                    Config.SaveConfigForLanguageChange();
                    ApplyCultureInfo(Localization.Config.I18N_Language_AutoDetect);
                    Config.LoadConfigForLanguageChange();
                    AnsiConsole.WriteLine(I18N_Options_MultipleLanguagesSelected);
                    Thread.Sleep(3000);
                }
                else if (selectedLanguage.Key != previousLanguage)
                {
                    Config.SaveConfigForLanguageChange();
                    ApplyCultureInfo(languages.First(x => Config.Get(x.Key)).Key);
                    Config.LoadConfigForLanguageChange();
                    //if (selectedLanguage.Key != Localization.Config.I18N_Language_AutoDetect)
                    //{
                    //    // 默认是true，所以不是自动检测的话就要再关掉
                    //    Config.Set(Localization.Config.I18N_Language_AutoDetect, false);
                    //}
                }

                Config.Save();
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
                    Config.Set(Localization.Config.I18N_EnableNetFilter, true);
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
                Config.Set(Localization.Config.I18N_EnableNetFilter, false);
            }
            else if (prompt == I18N_ConfigProxyServer)
            {
                string host;
                string port;
                string username;
                string password;
                do
                {
                    host = AnsiConsole.Prompt(new TextPrompt<string>(I18N_ConfigProxyServer_AskHost).AllowEmpty());
                    if (Uri.CheckHostName(host) == UriHostNameType.Dns)
                        host = Dns.GetHostAddresses(host)[0].ToString();
                    if (string.IsNullOrEmpty(host)) host = "127.0.0.1";
                } while (!IPAddress.TryParse(host, out var _));
                do
                {
                    port = AnsiConsole.Prompt(new TextPrompt<string>(I18N_ConfigProxyServer_AskPort).AllowEmpty());
                    if (string.IsNullOrEmpty(port)) port = "1080";
                } while (!int.TryParse(port, out var _));
                Config.Set(Localization.Config.I18N_ProxyHost, host);
                Config.Set(Localization.Config.I18N_ProxyPort, port);

                var proxyType = string.Empty;
                do
                {
                    proxyType = AnsiConsole.Ask<string>(I18N_ConfigProxyServer_AskType).ToLower();
                } while (proxyType[0] is not 's' and not 'h');
                Config.Set(Localization.Config.I18N_ProxyServerType, proxyType[0] == 'h' ? "http" : "socks");

                if (proxyType[0] == 's' && AnsiConsole.Confirm(I18N_ConfigProxyServer_AskAuth, false))
                {
                    do
                    {
                        username = AnsiConsole.Ask<string>(I18N_ConfigProxyServer_AskAuthUsername);
                    } while (string.IsNullOrEmpty(username));
                    do
                    {
                        password = AnsiConsole.Prompt(new TextPrompt<string>(I18N_ConfigProxyServer_AskAuthPassword).Secret());
                    } while (string.IsNullOrEmpty(password));
                    Config.Set(Localization.Config.I18N_ProxyUsername, username);
                    Config.Set(Localization.Config.I18N_ProxyPassword, password);
                }
                else
                {
                    Config.Set(Localization.Config.I18N_ProxyUsername, string.Empty);
                    Config.Set(Localization.Config.I18N_ProxyPassword, string.Empty);
                }
                Config.Save();
            }
            else if (prompt == I18N_InstallUraCore)
            {
                AnsiConsole.Clear();
                var uraCorePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "ura-core.dll");
                await ResourceUpdater.DownloadUraCore(uraCorePath);
                AnsiConsole.WriteLine(I18N_UraCoreHelper_FoundPaths, UraCoreHelper.GamePaths.Count);

                foreach (var i in UraCoreHelper.GamePaths)
                {
                    AnsiConsole.WriteLine(I18N_UraCoreHelper_FoundAvailablePath, i);
                    /// TODO
                    /// 判断umamusume.exe.local是否存在
                    ///     直接修改loadDll或者引导开启msgpackNotifier
                    /// 否则
                    ///     直接下载kimjio localify并安装（记得提醒安全风险）
                    ///     或引导使用TLG（记得免责声明）
                }
                Console.ReadKey();
            }
            else if (prompt == I18N_SetLocalizedDataFilePath)
            {
                AnsiConsole.Clear();
                AnsiConsole.MarkupLine(I18N_LocalizationData_FormatRequirement);
                string path;
                var valid = false;
                do
                {
                    path = AnsiConsole.Prompt(new TextPrompt<string>(I18N_LocalizationData_InputPathPrompt));
                    try
                    {
                        JsonConvert.DeserializeObject<Dictionary<TextDataCategory, Dictionary<int, string>>>(File.ReadAllText(path));
                        valid = true;
                    }
                    catch (Exception)
                    {
                        AnsiConsole.WriteLine(I18N_LocalizationData_FileCorrupt);
                        valid = false;
                        continue;
                    }
                } while (!valid);
                Config.Set(Localization.Config.I18N_LocalizedDataPath, path);
                Config.Save();
            }
            else if (prompt == I18N_ManageDMMService)
            {
                string dmmSelected;
                var dmmSelections = new List<string>([I18N_AddAccount, I18N_SetDeviceInfo, I18N_Cancel]);
                foreach (var i in DMM.Accounts)
                {
                    dmmSelections = dmmSelections.Prepend(i.Name).ToList();
                }
                do
                {
                    dmmSelected = AnsiConsole.Prompt(new SelectionPrompt<string>()
                        .Title(I18N_DeleteAccountInstruction)
                        .AddChoices(dmmSelections));

                    if (dmmSelected == I18N_AddAccount)
                    {
                        var actauth = AnsiConsole.Prompt(new TextPrompt<string>(I18N_InputActauthPrompt));
                        var savedataPath = AnsiConsole.Prompt(new TextPrompt<string>(I18N_InputSaveDataFilePathPrompt).AllowEmpty());
                        var executablePath = AnsiConsole.Prompt(new TextPrompt<string>(I18N_InputSplitUmamusumeFilePathPrompt).AllowEmpty());
                        var name = AnsiConsole.Prompt(new TextPrompt<string>(I18N_InputAccountCommentPrompt));

                        var account = new DMM.DMMAccount
                        {
                            actauth = actauth,
                            savedata_file_path = savedataPath,
                            split_umamusume_file_path = executablePath,
                            Name = name,
                        };

                        dmmSelections = dmmSelections.Prepend(name).ToList();
                        AnsiConsole.Clear();
                        DMM.Accounts.Add(account);
                        DMM.Save();
                    }
                    else if (dmmSelected == I18N_SetDeviceInfo)
                    {
                        var macAddress = AnsiConsole.Prompt(new TextPrompt<string>(string.Format(I18N_InputMacAddressPrompt, string.IsNullOrEmpty(DMM.mac_address) ? "无" : DMM.mac_address)).AllowEmpty());
                        var hddSerial = AnsiConsole.Prompt(new TextPrompt<string>(string.Format(I18N_InputHddSerialPrompt, string.IsNullOrEmpty(DMM.hdd_serial) ? "无" : DMM.hdd_serial)).AllowEmpty());
                        var motherboard = AnsiConsole.Prompt(new TextPrompt<string>(string.Format(I18N_InputMotherboardPrompt, string.IsNullOrEmpty(DMM.motherboard) ? "无" : DMM.motherboard)).AllowEmpty());
                        var userOS = AnsiConsole.Prompt(new TextPrompt<string>(string.Format(I18N_InputUserOsPrompt, string.IsNullOrEmpty(DMM.user_os) ? "无" : DMM.user_os)).AllowEmpty());
                        var umamusumePath = AnsiConsole.Prompt(new TextPrompt<string>(string.Format(I18N_InputUmamusumeFilePathPrompt, string.IsNullOrEmpty(DMM.umamusume_file_path) ? "无" : DMM.umamusume_file_path)).AllowEmpty());

                        if (!string.IsNullOrEmpty(macAddress)) DMM.mac_address = macAddress;
                        if (!string.IsNullOrEmpty(hddSerial)) DMM.hdd_serial = hddSerial;
                        if (!string.IsNullOrEmpty(motherboard)) DMM.motherboard = motherboard;
                        if (!string.IsNullOrEmpty(userOS)) DMM.user_os = userOS;
                        if (!string.IsNullOrEmpty(umamusumePath))
                        {
                            DMM.umamusume_file_path = umamusumePath.EndsWith("umamusume.exe")
                                ? umamusumePath
                                : Directory.GetFiles(umamusumePath, "umamusume.exe", SearchOption.AllDirectories).First();
                        }

                        AnsiConsole.Clear();
                        DMM.Save();
                    }
                    else if (dmmSelected != I18N_Cancel)
                    {
                        DMM.Accounts.RemoveAt(DMM.Accounts.FindIndex(x => x.Name == dmmSelected));
                        dmmSelections.Remove(dmmSelected);
                        DMM.Save();
                    }
                } while (dmmSelected != I18N_Cancel);
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
                            case "-v":
                            case "--version":
                                {
                                    Console.Write(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
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
                            case "--cmder":
                                {
                                    runInCmder = true;
                                    return;
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
                                    System.IO.Compression.ZipFile.ExtractToDirectory(args[1], Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer"));
                                    return;
                                }
                            case "--get-dmm-onetime-token":
                                {
                                    Console.Write(await DMM.Accounts[int.Parse(args[1])].GetExecuteArgsAsync());
                                    Environment.Exit(0);
                                    return;
                                }
                        }
                        return;
                    }
            }
        }
        internal static void ApplyCultureInfo(string culture)
        {
            if (culture == Localization.Config.I18N_Language_AutoDetect)
            {
                Thread.CurrentThread.CurrentCulture = defaultUICulture;
                Thread.CurrentThread.CurrentUICulture = defaultUICulture;
            }
            else if (culture == Localization.Config.I18N_Language_English)
            {
                Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            }
            else if (culture == Localization.Config.I18N_Language_Japanese)
            {
                Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ja-JP");
                Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ja-JP");
            }
            else if (culture == Localization.Config.I18N_Language_SimplifiedChinese)
            {
                Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
                Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
            }
            foreach (var i in Assembly.GetExecutingAssembly().GetTypes().Where(x => x.IsClass && x.Namespace?.StartsWith("UmamusumeResponseAnalyzer.Localization") == true))
            {
                var rc = i?.GetField("resourceCulture", BindingFlags.NonPublic | BindingFlags.Static);
                if (rc == null) continue;
                rc.SetValue(rc, Thread.CurrentThread.CurrentUICulture);
            }
        }
    }

    /// <summary>
    ///     Adapted from
    ///     http://stackoverflow.com/questions/13656846/how-to-programmatic-disable-c-sharp-console-applications-quick-edit-mode
    /// </summary>
    internal static partial class ConsoleHelper
    {
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
    }
}