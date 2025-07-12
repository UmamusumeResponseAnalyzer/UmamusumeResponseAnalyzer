using Microsoft.Win32;
using Spectre.Console;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace UmamusumeResponseAnalyzer
{
    public static class UraCoreHelper
    {
        private static List<string> gamePaths = [];
        public static List<string> GamePaths
        {
            get
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || gamePaths.Count != 0) return gamePaths;
                var _gamePaths = new List<string>();
                try
                {
                    var muiCache = Registry.CurrentUser.OpenSubKey("Software")?.OpenSubKey("Classes")?.OpenSubKey("Local Settings")?.OpenSubKey("Software")?.OpenSubKey("Microsoft")?.OpenSubKey("Windows")?.OpenSubKey("Shell")?.OpenSubKey("MuiCache");
                    if (muiCache != null)
                    {
                        foreach (var i in muiCache.GetValueNames().Where(x => x.Contains("umamusume.exe")))
                        {
                            _gamePaths.Add(i[..i.IndexOf("umamusume.exe")]);
                        }
                    }
                }
                catch { }
                try
                {
                    var explorerFeatureUsage = Registry.CurrentUser.OpenSubKey("Software")?.OpenSubKey("Microsoft")?.OpenSubKey("Windows")?.OpenSubKey("CurrentVersion")?.OpenSubKey("Explorer")?.OpenSubKey("FeatureUsage")?.OpenSubKey("AppSwitched");
                    if (explorerFeatureUsage != null)
                    {
                        foreach (var i in explorerFeatureUsage.GetValueNames().Where(x => x.Contains("umamusume.exe")))
                        {
                            _gamePaths.Add(i[..i.IndexOf("umamusume.exe")]);
                        }
                    }
                }
                catch { }
                try
                {
                    var compatibilityAssistant = Registry.CurrentUser.OpenSubKey("Software")?.OpenSubKey("Microsoft")?.OpenSubKey("Windows NT")?.OpenSubKey("CurrentVersion")?.OpenSubKey("AppCompatFlags")?.OpenSubKey("Compatibility Assistant")?.OpenSubKey("Store");
                    if (compatibilityAssistant != null)
                    {
                        foreach (var i in compatibilityAssistant.GetValueNames().Where(x => x.Contains("umamusume.exe")))
                        {
                            _gamePaths.Add(i[..i.IndexOf("umamusume.exe")]);
                        }
                    }
                }
                catch { }
                try
                {
                    var gameConfigStore = Registry.CurrentUser.OpenSubKey("System")?.OpenSubKey("GameConfigStore")?.OpenSubKey("Children");
                    if (gameConfigStore != null)
                    {
                        foreach (var subkey in gameConfigStore.GetSubKeyNames())
                        {
                            var gameConfig = gameConfigStore.OpenSubKey(subkey);
                            if (gameConfig != null)
                            {
                                var value = gameConfig.GetValue("MatchedExeFullPath");
                                if (value is string path && path.Contains("umamusume.exe"))
                                {
                                    _gamePaths.Add(path[..path.IndexOf("umamusume.exe")]);
                                }
                            }
                        }
                    }
                }
                catch { }
                gamePaths = [.. _gamePaths.Distinct()];
                return gamePaths;
            }
            set
            {
                gamePaths = value;
            }
        }
        public static void EnableDllRedirection()
        {
            using var registry = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", true);
            if (registry == null)
            {
                AnsiConsole.WriteLine("打开注册表失败，请手动操作：https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-redirection#optional-configure-the-registry");
                Thread.Sleep(int.MaxValue);
                return;
            }
            if (registry.GetValue("DevOverrideEnable") is int current and not 1)
            {
                var registryCaution = new ConfirmationPrompt(@"该行为具有一定风险，将注册表HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\DevOverrideEnable的值改为1。
请仔细阅读https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-redirection 了解其风险后再做决定，我们不对此负责。");
                if (AnsiConsole.Prompt(registryCaution))
                {
                    registry.SetValue("DevOverrideEnable", 1, RegistryValueKind.DWord);
                    if (registry.GetValue("DevOverrideEnable") is int value && value == 1)
                    {
                        AnsiConsole.WriteLine("已启用DLL重定向，请手动重启Windows使其生效。");
                    }
                    else
                    {
                        AnsiConsole.WriteLine("注册表启用DLL重定向失败，请手动检查。");
                        Environment.ExitCode = 1;
                    }
                }
            }
            else
            {
                AnsiConsole.WriteLine("注册表已启用DLL重定向，将在三秒后自动关闭。没有做任何改动。");
            }
        }
    }
}