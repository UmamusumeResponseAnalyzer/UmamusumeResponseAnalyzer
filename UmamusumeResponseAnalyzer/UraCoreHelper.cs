using Microsoft.Win32;
using Spectre.Console;
using System.Runtime.InteropServices;

namespace UmamusumeResponseAnalyzer
{
    public static class UraCoreHelper
    {
        private const string GameExeName = "umamusume.exe";
        private static readonly Lazy<List<string>> _lazyGamePaths = new(LoadGamePaths, LazyThreadSafetyMode.ExecutionAndPublication);
        private static List<string> _overrideGamePaths = [];

        public static List<string> GamePaths
        {
            get => _overrideGamePaths.Count != 0 ? _overrideGamePaths : _lazyGamePaths.Value;
            set => _overrideGamePaths = value;
        }

        private static List<string> LoadGamePaths()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return [];

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // MuiCache — value names that are full exe paths
            TryExtractFromValueNames(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
                Registry.CurrentUser,
                paths);

            // Explorer AppSwitched — same structure
            TryExtractFromValueNames(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched",
                Registry.CurrentUser,
                paths);

            // AppCompatFlags Compatibility Assistant Store — same structure
            TryExtractFromValueNames(
                @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
                Registry.CurrentUser,
                paths);

            // GameConfigStore — each child subkey has MatchedExeFullPath value
            TryExtractFromGameConfigStore(paths);

            return [.. paths];
        }

        /// <summary>
        /// Scans a registry key whose value *names* are full exe paths (e.g. MuiCache).
        /// </summary>
        private static void TryExtractFromValueNames(string subKeyPath, RegistryKey hive, HashSet<string> results)
        {
            try
            {
                using var key = OpenSubKey(hive, subKeyPath);
                if (key is null) return;

                foreach (var name in key.GetValueNames())
                {
                    var idx = name.IndexOf(GameExeName, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                        results.Add(name[..idx]);
                }
            }
            catch { }
        }

        private static void TryExtractFromGameConfigStore(HashSet<string> results)
        {
            try
            {
                using var storeKey = OpenSubKey(Registry.CurrentUser, @"System\GameConfigStore\Children");
                if (storeKey is null) return;

                foreach (var subkeyName in storeKey.GetSubKeyNames())
                {
                    try
                    {
                        using var child = storeKey.OpenSubKey(subkeyName);
                        if (child?.GetValue("MatchedExeFullPath") is string path)
                        {
                            var idx = path.IndexOf(GameExeName, StringComparison.OrdinalIgnoreCase);
                            if (idx >= 0)
                                results.Add(path[..idx]);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Opens a registry sub-key by a backslash-separated relative path under <paramref name="hive"/>.
        /// Returns <see langword="null"/> if any segment is missing.
        /// </summary>
        private static RegistryKey? OpenSubKey(RegistryKey hive, string relativePath)
        {
            var current = hive;
            foreach (var segment in relativePath.Split('\\'))
            {
                var next = current?.OpenSubKey(segment);
                if (current != hive) current?.Dispose(); // Don't dispose the caller-owned hive
                current = next;
                if (current is null) return null;
            }
            return current;
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

            // Only skip the prompt when the value is already correctly set to 1.
            if (registry.GetValue("DevOverrideEnable") is int current && current == 1)
            {
                AnsiConsole.WriteLine("注册表已启用DLL重定向，将在三秒后自动关闭。没有做任何改动。");
                return;
            }

            var registryCaution = new ConfirmationPrompt(
                @"该行为具有一定风险，将注册表HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\DevOverrideEnable的值改为1。" + "\n" +
                "请仔细阅读https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-redirection 了解其风险后再做决定，我们不对此负责。");

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
    }
}