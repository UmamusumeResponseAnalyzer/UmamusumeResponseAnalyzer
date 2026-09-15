using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed record PluginUpdateInfo(string DisplayName, Version CurrentVersion, Version LatestVersion);

internal static class PluginRepository
{
    internal const string PluginApiBase = "https://ura.shuise.net/api/Plugins";
    const long MaxPackageBytes = 64L * 1024 * 1024;
    static readonly System.Resources.ResourceManager Resources = new(
        "UmamusumeResponseAnalyzer.Localization.PluginRegistry", typeof(PluginRepository).Assembly);
    const string UncategorizedLabel = "其他";

    internal static string Text(string name) => Resources.GetString(name,
        System.Globalization.CultureInfo.GetCultureInfo(LanguageConfig.GetCulture()))!;

    public static async Task ShowMenuAsync(CancellationToken cancellationToken)
    {
        try
        {
            var plugins = BuildCatalog(await FetchPluginsAsync(
                PluginApiBase, cancellationToken), Config.Repository.Targets);
            if (plugins.Count == 0)
            {
                TerminalUi.Acknowledge(Text("Empty"), cancellationToken);
                return;
            }
            var choices = plugins
                .OrderBy(p => string.IsNullOrWhiteSpace(p.Category) || p.Category == UncategorizedLabel ? 1 : 0)
                .ThenBy(p => string.IsNullOrWhiteSpace(p.Category) ? UncategorizedLabel : p.Category)
                .ThenBy(DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var selected = TerminalUi.MultiSelect(Text("SelectPlugins"), choices,
                converter: FormatChoice, cancellationToken: cancellationToken).ToList();
            var installed = await InstallPluginsAsync(selected, cancellationToken);
            if (installed.Count == 0)
                return;

            var results = await PluginManager.ReloadPluginsAsync([.. installed]);
            cancellationToken.ThrowIfCancellationRequested();
            var failed = results.Where(r => r.Outcome == PluginManager.PluginLifecycleOutcome.Failed)
                .Select(r => r.PluginName).ToArray();
            if (failed.Length > 0)
                throw new InvalidOperationException($"插件安装完成，但加载失败：{string.Join("、", failed)}");
            TerminalUi.Acknowledge($"插件已安装并生效：{string.Join("、", installed)}", cancellationToken);
        }
        catch (global::UmamusumeResponseAnalyzer.UmamusumeResponseAnalyzer.PostShutdownProcessRequestedException) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { TerminalUi.Acknowledge($"{Text("Failed")}: {ex.Message}", cancellationToken); }
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
            throw new InvalidOperationException("已选插件 InternalName 重复。 / Selected plugins share an InternalName.");
        var installed = new List<string>();
        foreach (var plugin in selected)
        {
            try
            {
                TerminalUi.Log("URA", $"[{DisplayLabel(plugin)} v{plugin.RawVersion}] 正在下载");
                var manifest = await DownloadPluginZipAsync(plugin, cancellationToken);
                installed.Add(manifest.InternalName);
                TerminalUi.Log("URA", $"[{DisplayLabel(manifest)}] 安装完成");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var message = $"[{DisplayLabel(plugin)}] 安装失败: {ex.Message}";
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
                throw new InvalidDataException($"插件 InternalName 对应多个来源，无法判断更新: {plugin.InternalName}");
        var updates = new List<PluginUpdateInfo>();
        foreach (var plugin in loaded)
        {
            var version = plugin.Version
                ?? throw new InvalidOperationException($"已加载插件缺少版本: {plugin.InternalName}");
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
            throw new InvalidDataException("插件下载引用无效。 / Invalid plugin download reference.");
        var plugin = data["manifest"]?.ToObject<PluginInformation>()
            ?? throw new InvalidDataException("插件 manifest 缺失。 / Missing plugin manifest.");
        PluginPackageValidator.ValidateManifest(plugin);
        plugin.DownloadUrl = $"{PluginApiBase}/{repositoryId}/releases/{releaseId}/download";
        return plugin;
    }

    internal static async Task<PluginInformation> GetPluginAsync(long repositoryId, long releaseId, CancellationToken cancellationToken)
    {
        if (repositoryId <= 0 || releaseId <= 0)
            throw new ArgumentException("非法的插件来源 ID。 / Invalid plugin source ID.");
        var url = $"{PluginApiBase}/{repositoryId}/releases/{releaseId}";
        var plugin = ReadPlugin(await FetchAsync<JObject>(url, cancellationToken));
        if (plugin.DownloadUrl != $"{url}/download")
            throw new InvalidDataException("插件下载引用与请求不匹配。 / Plugin download reference does not match the request.");
        return plugin;
    }

    internal static async Task<PluginInformation> DownloadPluginZipAsync(PluginInformation plugin, CancellationToken cancellationToken)
    {
        using var response = await ResourceUpdater.HttpClient.GetAsync(plugin.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxPackageBytes) throw new InvalidDataException("插件 ZIP 超过 64 MiB。");
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
                    if (total > MaxPackageBytes) throw new InvalidDataException("插件 ZIP 超过 64 MiB。");
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
            ?? throw new InvalidDataException("URACloud 返回了空 JSON。");
    }

    internal static PluginInformation ValidatePackage(string path, string expectedAuthor, string expectedInternalName, string expectedVersion)
    {
        var manifest = PluginPackageValidator.Validate(path, requireMatchingPackageFileName: false).Manifest;
        if (!manifest.Author.Equals(expectedAuthor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"下载包 Author 与请求不匹配: expected={expectedAuthor}, actual={manifest.Author}");
        if (!manifest.InternalName.Equals(expectedInternalName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"下载包 InternalName 与请求不匹配: expected={expectedInternalName}, actual={manifest.InternalName}");
        if (!Version.TryParse(expectedVersion, out var version) || manifest.Version != version)
            throw new InvalidDataException($"下载包 Version 与请求不匹配: expected={expectedVersion}, actual={manifest.RawVersion}");
        return manifest;
    }
}
