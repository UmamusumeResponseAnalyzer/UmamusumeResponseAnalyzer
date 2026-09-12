using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security;
using System.Runtime.Versioning;

namespace UmamusumeResponseAnalyzer
{
    public static class UraCoreHelper
    {
        public static IReadOnlyList<string> GamePaths => LoadGamePaths();

        /// <summary>
        /// Registry value names may append a MuiCache property after the executable name.
        /// </summary>
        internal static string? ExtractGamePathPrefix(string candidate)
        {
            foreach (var name in HachimiEdgeGame.ExecutableNames)
            {
                var start = 0;
                while (candidate.IndexOf(name, start, StringComparison.OrdinalIgnoreCase) is var index && index >= 0)
                {
                    var end = index + name.Length;
                    if ((index == 0 || candidate[index - 1] is '/' or '\\') &&
                        (end == candidate.Length || candidate[end] == '.'))
                        return candidate[..index];
                    start = end;
                }
            }
            return null;
        }

        internal static IReadOnlyList<string> LoadGamePaths(List<string>? warnings = null)
        {
            if (!OperatingSystem.IsWindows())
                return [];

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // MuiCache — value names that are full exe paths
            TryExtractFromValueNames(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
                Registry.CurrentUser,
                paths, warnings);

            // Explorer AppSwitched — same structure
            TryExtractFromValueNames(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched",
                Registry.CurrentUser,
                paths, warnings);

            // AppCompatFlags Compatibility Assistant Store — same structure
            TryExtractFromValueNames(
                @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
                Registry.CurrentUser,
                paths, warnings);

            // GameConfigStore — each child subkey has MatchedExeFullPath value
            TryExtractFromGameConfigStore(paths, warnings);
            var dmmPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dmmgameplayer5", "dmmgame.cnf");
            try
            {
                if (File.Exists(dmmPath))
                {
                    var config = JObject.Parse(File.ReadAllText(dmmPath));
                    foreach (var game in config["contents"] as JArray ?? [])
                        if ((string?)game["productId"] == "umamusume" && (string?)game["detail"]?["path"] is { Length: > 0 } path)
                            paths.Add(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                warnings?.Add($"{dmmPath}: {ex.Message}");
            }
            try
            {
                using var komoe = Registry.CurrentUser.OpenSubKey(@"Software\komoemumamusume");
                if (komoe?.GetValue("GameInstallPath") is string path && path.Length > 0)
                    paths.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                warnings?.Add($"Komoe registry: {ex.Message}");
            }

            return Array.AsReadOnly(paths
                .Select(path => Path.TrimEndingDirectorySeparator(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        /// <summary>
        /// Scans a registry key whose value *names* are full exe paths (e.g. MuiCache).
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static void TryExtractFromValueNames(string subKeyPath, RegistryKey hive, HashSet<string> results, List<string>? warnings)
        {
            try
            {
                using var key = hive.OpenSubKey(subKeyPath);
                if (key is null) return;

                foreach (var name in key.GetValueNames())
                {
                    if (ExtractGamePathPrefix(name) is { } prefix)
                        results.Add(prefix);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                warnings?.Add($"{subKeyPath}: {ex.Message}");
            }
        }

        [SupportedOSPlatform("windows")]
        private static void TryExtractFromGameConfigStore(HashSet<string> results, List<string>? warnings)
        {
            try
            {
                using var storeKey = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children");
                if (storeKey is null) return;

                foreach (var subkeyName in storeKey.GetSubKeyNames())
                {
                    try
                    {
                        using var child = storeKey.OpenSubKey(subkeyName);
                        if (child?.GetValue("MatchedExeFullPath") is string path
                            && ExtractGamePathPrefix(path) is { } prefix)
                        {
                            results.Add(prefix);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        warnings?.Add($"GameConfigStore/{subkeyName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                warnings?.Add($"GameConfigStore: {ex.Message}");
            }
        }

        public static void EnableDllRedirection()
        {
            Environment.ExitCode = 1;
            var changed = HachimiEdgeInstallation.EnableDllRedirection();
            Console.WriteLine(changed
                ? "已启用 DLL redirection，请手动重启 Windows 使其生效。"
                : "注册表已启用 DLL redirection，没有做任何改动。");
            Environment.ExitCode = 0;
        }
    }
}
