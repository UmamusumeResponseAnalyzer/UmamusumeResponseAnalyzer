using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed record PluginUpdateInfo(string DisplayName, Version CurrentVersion, Version LatestVersion);

internal static class PluginRepository
{
    internal const string PluginApiBase = "https://ura.shuise.net/api/Plugins";
    const long MaxPackageBytes = 64L * 1024 * 1024;
    const string UncategorizedCategory = "其他";

    public static async Task ShowMenuAsync(CancellationToken cancellationToken)
    {
        try
        {
            var plugins = BuildCatalog(await FetchPluginsAsync(
                PluginApiBase, cancellationToken), Config.Repository.Targets);
            if (plugins.Count == 0)
            {
                ModalDialogs.Acknowledge(i18n.Empty, cancellationToken);
                return;
            }
            var choices = plugins
                .OrderBy(p => string.IsNullOrWhiteSpace(p.Category) || p.Category == UncategorizedCategory ? 1 : 0)
                .ThenBy(p => string.IsNullOrWhiteSpace(p.Category) ? UncategorizedCategory : p.Category)
                .ThenBy(DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var selected = ModalDialogs.MultiSelect(i18n.SelectPlugins, choices,
                converter: FormatChoice, cancellationToken: cancellationToken).ToList();
            var installed = await InstallPluginsAsync(selected, cancellationToken);
            if (installed.Count == 0)
                return;

            ModalDialogs.Acknowledge(string.Format(i18n.InstalledRestartRequired, string.Join(i18n.ListSeparator, installed)), cancellationToken);
        }
        catch (global::UmamusumeResponseAnalyzer.UmamusumeResponseAnalyzer.PostShutdownProcessRequestedException) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { ModalDialogs.Acknowledge(string.Format(i18n.OperationFailed, ex.Message), cancellationToken); }
    }

    internal static List<PluginInformation> BuildCatalog(IEnumerable<PluginInformation> raw, IReadOnlyCollection<string> targets) =>
        raw.Where(p => targets.Count == 0 || p.Targets.Length == 0 || p.Targets.Intersect(targets).Any()).ToList();

    static string DisplayLabel(PluginInformation manifest) =>
        string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.InternalName : manifest.DisplayName;

    static string FormatChoice(PluginInformation plugin) =>
        ($"{DisplayLabel(plugin)} v{plugin.RawVersion} [{plugin.RepositoryUrl}]" +
        (string.IsNullOrEmpty(plugin.Description) ? "" : $" — {plugin.Description}")).ReplaceLineEndings(" ");

    internal static async Task<List<string>> InstallPluginsAsync(List<PluginInformation> selected, CancellationToken cancellationToken = default)
    {
        if (selected.Select(p => p.InternalName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Count)
            throw new InvalidOperationException(i18n.DuplicateSelection);
        var installed = new List<string>();
        foreach (var plugin in selected)
        {
            try
            {
                TerminalUi.Log("URA", string.Format(i18n.Downloading, DisplayLabel(plugin), plugin.RawVersion));
                var manifest = await DownloadPluginZipAsync(plugin, cancellationToken);
                installed.Add(manifest.InternalName);
                TerminalUi.Log("URA", string.Format(i18n.Installed, DisplayLabel(manifest)));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var message = string.Format(i18n.InstallFailed, DisplayLabel(plugin), ex.Message);
                TerminalUi.Log("URA", message, UiSeverity.Error);
                TerminalUi.Notify("URA", message, UiSeverity.Error);
            }
        }
        return installed;
    }

    public static async Task<IReadOnlyList<PluginUpdateInfo>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var loaded = PluginManager.SnapshotPluginStatuses().Where(p => p.IsLoaded).ToArray();
        if (loaded.Length == 0)
            return [];
        var remote = BuildCatalog(await FetchPluginsAsync(PluginApiBase, cancellationToken), Config.Repository.Targets);
        var remoteByName = new Dictionary<string, PluginInformation>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in remote)
            if (!remoteByName.TryAdd(plugin.InternalName, plugin))
                throw new InvalidDataException(string.Format(i18n.AmbiguousSource, plugin.InternalName));
        var updates = new List<PluginUpdateInfo>();
        foreach (var plugin in loaded)
        {
            var version = plugin.Version
                ?? throw new InvalidOperationException(string.Format(i18n.LoadedVersionMissing, plugin.InternalName));
            if (remoteByName.TryGetValue(plugin.InternalName, out var latest) && latest.Version > version)
                updates.Add(new(DisplayLabel(latest), version, latest.Version));
        }
        return updates;
    }

    internal static string InstallZipPath(string internalName) => Path.Combine("Plugins", $"{internalName}.zip");

    static async Task<List<PluginInformation>> FetchPluginsAsync(string url, CancellationToken cancellationToken) =>
        (await FetchAsync<JArray>(url, cancellationToken)).Select(ReadPlugin).ToList();

    static PluginInformation ReadPlugin(JToken data)
    {
        var repositoryId = data["source"]?["repositoryId"]?.Value<long>();
        var releaseId = data["releaseId"]?.Value<long>();
        if (repositoryId is not > 0 || releaseId is not > 0)
            throw new InvalidDataException(i18n.InvalidDownloadReference);
        var plugin = data["manifest"]?.ToObject<PluginInformation>()
            ?? throw new InvalidDataException(i18n.ManifestMissing);
        PluginPackageValidator.ValidateManifest(plugin);
        plugin.DownloadUrl = $"{PluginApiBase}/{repositoryId}/releases/{releaseId}/download";
        return plugin;
    }

    internal static async Task<PluginInformation> GetPluginAsync(long repositoryId, long releaseId, CancellationToken cancellationToken)
    {
        if (repositoryId <= 0 || releaseId <= 0)
            throw new ArgumentException(i18n.InvalidSourceId);
        var url = $"{PluginApiBase}/{repositoryId}/releases/{releaseId}";
        var plugin = ReadPlugin(await FetchAsync<JObject>(url, cancellationToken));
        if (plugin.DownloadUrl != $"{url}/download")
            throw new InvalidDataException(i18n.DownloadReferenceMismatch);
        return plugin;
    }

    internal static async Task<PluginInformation> DownloadPluginZipAsync(PluginInformation plugin, CancellationToken cancellationToken)
    {
        using var response = await ResourceUpdater.HttpClient.GetAsync(plugin.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxPackageBytes) throw new InvalidDataException(i18n.PackageTooLarge);
        Directory.CreateDirectory("Plugins");
        var temp = Path.Combine("Plugins", $"plugin-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                var total = 0L;
                for (var count = await input.ReadAsync(buffer, cancellationToken); count != 0; count = await input.ReadAsync(buffer, cancellationToken))
                {
                    total += count;
                    if (total > MaxPackageBytes) throw new InvalidDataException(i18n.PackageTooLarge);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            var manifest = ValidatePackage(temp, plugin.Author, plugin.InternalName, plugin.RawVersion);
            cancellationToken.ThrowIfCancellationRequested();
            var existing = Directory.GetFiles("Plugins", "*.zip").SingleOrDefault(p =>
                Path.GetFileNameWithoutExtension(p).Equals(manifest.InternalName, StringComparison.OrdinalIgnoreCase));
            File.Move(temp, existing ?? InstallZipPath(manifest.InternalName), overwrite: true);
            return manifest;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static async Task<T> FetchAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await ResourceUpdater.HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync(cancellationToken),
            new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })
            ?? throw new InvalidDataException(i18n.EmptyJson);
    }

    internal static PluginInformation ValidatePackage(string path, string expectedAuthor, string expectedInternalName, string expectedVersion)
    {
        var manifest = PluginPackageValidator.Validate(path, requireMatchingPackageFileName: false).Manifest;
        if (!manifest.Author.Equals(expectedAuthor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(string.Format(i18n.PackageAuthorMismatch, expectedAuthor, manifest.Author));
        if (!manifest.InternalName.Equals(expectedInternalName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(string.Format(i18n.PackageInternalNameMismatch, expectedInternalName, manifest.InternalName));
        if (!Version.TryParse(expectedVersion, out var version) || manifest.Version != version)
            throw new InvalidDataException(string.Format(i18n.PackageVersionMismatch, expectedVersion, manifest.RawVersion));
        return manifest;
    }
}
