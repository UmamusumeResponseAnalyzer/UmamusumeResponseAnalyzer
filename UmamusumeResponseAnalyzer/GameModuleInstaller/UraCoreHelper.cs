using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security;
using System.Runtime.Versioning;
using static UmamusumeResponseAnalyzer.Localization.Dmm;

namespace UmamusumeResponseAnalyzer
{
    public static class UraCoreHelper
    {
        public static IReadOnlyList<string> GamePaths => LoadGamePaths();

        /// <summary>
        /// Reads the official DMM installation record. Returns null when no installed game is recorded.
        /// </summary>
        /// <exception cref="InvalidDataException">The installation record or game location is invalid.</exception>
        public static string? FindDmmGameExecutable(string? installationFile = null)
        {
            installationFile ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "dmmgameplayer5", "dmmgame.cnf");
            try
            {
                var config = JObject.Parse(File.ReadAllText(installationFile));
                if (config["contents"] is not JArray contents)
                    throw new InvalidDataException(I18N_DmmPath_InvalidRecords);

                var matches = new List<JObject>();
                foreach (var entry in contents)
                {
                    if (entry is not JObject game)
                        throw new InvalidDataException(I18N_DmmPath_InvalidRecords);
                    if (game["productId"] is not JValue { Value: "umamusume" }
                        || game["gameType"] is not JValue { Value: "GCL" })
                        continue;
                    if (game["detail"] is not JObject detail
                        || detail["installed"] is not JValue { Type: JTokenType.Boolean, Value: bool installed })
                        throw new InvalidDataException(I18N_DmmPath_InvalidRecords);
                    if (installed) matches.Add(detail);
                }
                if (matches.Count == 0) return null;
                if (matches.Count > 1)
                    throw new InvalidDataException(string.Format(I18N_DmmPath_RecordCount, matches.Count));
                if (matches[0]["path"] is not JValue { Type: JTokenType.String, Value: string directory }
                    || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
                    throw new InvalidDataException(I18N_DmmPath_InvalidDirectory);

                var executable = Path.GetFullPath(Path.Combine(directory, "umamusume.exe"));
                if (!File.Exists(executable))
                    throw new InvalidDataException(string.Format(I18N_DmmPath_ExecutableMissing, executable));
                return executable;
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                or JsonException or ArgumentException or NotSupportedException or SecurityException)
            {
                throw new InvalidDataException(string.Format(I18N_DmmPath_DiscoveryFailed, installationFile, ex.Message), ex);
            }
        }

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

        internal static IReadOnlyList<string> LoadGamePaths(List<string>? warnings = null,
            string? dmmInstallationFile = null, RegistryKey? currentUser = null)
        {
            if (!OperatingSystem.IsWindows())
                return [];

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            currentUser ??= Registry.CurrentUser;

            // MuiCache — value names that are full exe paths
            TryExtractFromValueNames(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
                currentUser,
                paths, warnings);

            // Explorer AppSwitched — same structure
            TryExtractFromValueNames(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched",
                currentUser,
                paths, warnings);

            // AppCompatFlags Compatibility Assistant Store — same structure
            TryExtractFromValueNames(
                @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
                currentUser,
                paths, warnings);

            // GameConfigStore — each child subkey has MatchedExeFullPath value
            TryExtractFromGameConfigStore(currentUser, paths, warnings);
            try
            {
                if (FindDmmGameExecutable(dmmInstallationFile) is { } executable)
                    paths.Add(Path.GetDirectoryName(executable)!);
            }
            catch (InvalidDataException ex)
            {
                warnings?.Add(ex.Message);
            }
            try
            {
                using var komoe = currentUser.OpenSubKey(@"Software\komoemumamusume");
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
        private static void TryExtractFromGameConfigStore(RegistryKey hive, HashSet<string> results, List<string>? warnings)
        {
            try
            {
                using var storeKey = hive.OpenSubKey(@"System\GameConfigStore\Children");
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
