using Newtonsoft.Json;
using Spectre.Console;
using System.IO.Compression;
using UmamusumeResponseAnalyzer.LiveDisplay;

namespace UmamusumeResponseAnalyzer.Plugin
{
    public sealed record PluginUpdateInfo(
        string InternalName,
        string DisplayName,
        Version CurrentVersion,
        Version LatestVersion);

    public static class PluginRepository
    {
        const string PluginApiBase = "https://ura.shuise.net/Plugins";
        static readonly Version ZeroVersion = new(0, 0, 0);

        public static async Task ShowMenuAsync()
        {
            await LiveDisplayConsole.RunAsync(ShowMenuCoreAsync);
        }

        static async Task ShowMenuCoreAsync()
        {
            var plugins = await FetchAllPluginsAsync();
            LiveDisplayConsole.Clear();
            if (plugins.Count == 0)
            {
                LiveDisplayConsole.MarkupLine("[yellow]没有从插件仓库拉到插件信息。[/]");
                LiveDisplayConsole.MarkupLine("按任意键返回");
                LiveDisplayConsole.ReadKey(intercept: true);
                return;
            }

            var pluginSelection = new MultiSelectionPrompt<string>()
                .Title("选择要安装的插件")
                .WrapAround(true)
                .PageSize(30)
                .NotRequired();

            var labelToInfo = new Dictionary<string, PluginInformation>(StringComparer.Ordinal);
            var grouped = plugins.Values
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? UncategorizedLabel : p.Category)
                .OrderBy(g => CategoryOrder(g.Key))
                .ThenBy(g => g.Key);
            foreach (var group in grouped)
            {
                var leafLabels = new List<string>();
                foreach (var info in group.OrderBy(DisplayLabel, StringComparer.OrdinalIgnoreCase))
                {
                    var label = FormatChoice(info);
                    leafLabels.Add(label);
                    labelToInfo[label] = info;
                }
                pluginSelection.AddChoiceGroup($"[blue]{group.Key.EscapeMarkup()}[/]", leafLabels);
            }

            var selected = LiveDisplayConsole.Prompt(pluginSelection);
            var selectedPlugins = selected
                .Where(labelToInfo.ContainsKey)
                .Select(label => labelToInfo[label])
                .ToList();
            if (selectedPlugins.Count == 0)
            {
                LiveDisplayConsole.Clear();
                return;
            }

            ResolveDependencies(selectedPlugins, plugins);
            var installed = await InstallPluginsAsync(selectedPlugins);

            if (installed > 0)
            {
                LiveDisplayConsole.WriteLine("插件安装已全部完成，按任意键重新启动。");
                LiveDisplayConsole.ReadKey();
                global::UmamusumeResponseAnalyzer.UmamusumeResponseAnalyzer.Restart();
            }
        }

        public static async Task<IReadOnlyList<PluginUpdateInfo>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            if (PluginManager.LoadedPlugins.Count == 0) return [];
            var remote = await FetchAllPluginsAsync(silent: true, cancellationToken: cancellationToken, throwOnError: true);
            if (remote.Count == 0) return [];

            var updates = new List<PluginUpdateInfo>();
            foreach (var plugin in PluginManager.LoadedPlugins)
            {
                var assemblyName = plugin.GetType().Assembly.GetName().Name;
                if (string.IsNullOrEmpty(assemblyName)) continue;
                if (!remote.TryGetValue(assemblyName, out var remoteInfo)) continue;
                if (remoteInfo.Version > plugin.Version)
                {
                    updates.Add(new PluginUpdateInfo(
                        assemblyName,
                        DisplayLabel(remoteInfo),
                        plugin.Version,
                        remoteInfo.Version));
                }
            }

            return updates;
        }

        static async Task<Dictionary<string, PluginInformation>> FetchAllPluginsAsync(bool silent = false, CancellationToken cancellationToken = default, bool throwOnError = false)
        {
            if (!silent) LiveDisplayConsole.WriteLine("正在从插件仓库获取插件信息");
            var list = await FetchAsync(PluginApiBase, cancellationToken, silent, throwOnError);

            var plugins = new Dictionary<string, PluginInformation>(StringComparer.OrdinalIgnoreCase);
            var noTargetFilter = Config.Repository.Targets.Count == 0;
            foreach (var plugin in list.Where(x =>
                !string.IsNullOrEmpty(x.InternalName) && !string.IsNullOrEmpty(x.Author)
                && (noTargetFilter || x.Targets.Length == 0 || x.Targets.Intersect(Config.Repository.Targets).Any())))
            {
                plugin.DownloadUrl = $"{PluginApiBase}/{plugin.Author}/{plugin.InternalName}/versions/{plugin.Version}/download";
                plugins[plugin.InternalName] = plugin;
            }
            return plugins;
        }

        static async Task<List<PluginInformation>> FetchAsync(string url, CancellationToken cancellationToken, bool silent = false, bool throwOnError = false)
        {
            try
            {
                var resp = await ResourceUpdater.HttpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken);
                resp.EnsureSuccessStatusCode();
                var text = await resp.Content.ReadAsStringAsync(cancellationToken);
                return JsonConvert.DeserializeObject<List<PluginInformation>>(text) ?? [];
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (throwOnError)
                    throw;

                if (!silent)
                    LiveDisplayConsole.MarkupLine($"[red]从 {url.EscapeMarkup()} 拉取仓库失败:[/] {ex.Message.EscapeMarkup()}");
                return [];
            }
        }

        const string UncategorizedLabel = "未分类";
        static int CategoryOrder(string category) => category == UncategorizedLabel ? 1 : 0;

        static string DisplayLabel(PluginInformation info) =>
            string.IsNullOrWhiteSpace(info.DisplayName) ? info.InternalName : info.DisplayName;

        static string FormatChoice(PluginInformation info)
        {
            var version = info.Version == ZeroVersion ? "" : $" v{info.Version}";
            var desc = string.IsNullOrEmpty(info.Description) ? "" : $" — {info.Description}";
            return $"{DisplayLabel(info)}{version}{desc}";
        }

        static void ResolveDependencies(List<PluginInformation> selectedPlugins, Dictionary<string, PluginInformation> plugins)
        {
#warning TODO: 递归解析传递依赖（当前只展开一层）
            var dependencies = selectedPlugins.SelectMany(x => x.Dependencies).Distinct().ToArray();
            foreach (var dependency in dependencies)
            {
                if (selectedPlugins.Any(x => string.Equals(x.InternalName, dependency, StringComparison.OrdinalIgnoreCase))) continue;

                if (plugins.TryGetValue(dependency, out var dependencyPluginInfo))
                {
                    selectedPlugins.Add(dependencyPluginInfo);
                }
                else
                {
                    LiveDisplayConsole.WriteLine($"没有在插件仓库中找到依赖项{dependency}");
                }
            }
        }

        static string VersionsUrl(PluginInformation plugin) =>
            $"{PluginApiBase}/{plugin.Author}/{plugin.InternalName}/versions";

        static async Task<PluginInformation?> PromptVersionAsync(PluginInformation plugin, CancellationToken cancellationToken)
        {
            var versions = await FetchAsync(VersionsUrl(plugin), cancellationToken);
            // 版本 manifest 不带 DownloadUrl——用原始 plugin 的 Author/InternalName 拼，不信任 response 字段
            foreach (var v in versions)
                v.DownloadUrl = $"{PluginApiBase}/{plugin.Author}/{plugin.InternalName}/versions/{v.Version}/download";
            versions = versions
                .Where(v => v.Version > ZeroVersion)
                .OrderByDescending(v => v.Version)
                .ToList();
            if (versions.Count == 0)
            {
                return plugin;
            }
            if (versions.Count == 1)
            {
                return versions[0];
            }

            const string CancelLabel = "取消该插件";
            var labelToVersion = new Dictionary<string, PluginInformation>(StringComparer.Ordinal);
            for (var i = 0; i < versions.Count; i++)
            {
                var v = versions[i];
                var tag = i == 0 ? " [grey](最新)[/]" : string.Empty;
                var changelog = string.IsNullOrEmpty(v.Changelog) ? string.Empty : $" — {v.Changelog.EscapeMarkup()}";
                labelToVersion[$"{v.Version}{tag}{changelog}"] = v;
            }

            var selection = LiveDisplayConsole.Prompt(new SelectionPrompt<string>()
                .Title($"选择 [green]{DisplayLabel(plugin).EscapeMarkup()}[/] 要安装的版本")
                .WrapAround(true)
                .PageSize(15)
                .AddChoices([.. labelToVersion.Keys, CancelLabel]));
            if (selection == CancelLabel) return null;
            return labelToVersion[selection];
        }

        static async Task<int> InstallPluginsAsync(List<PluginInformation> selectedPlugins, CancellationToken cancellationToken = default)
        {
            var installed = 0;
            Directory.CreateDirectory("Plugins");
            foreach (var plugin in selectedPlugins)
            {
                var versionToInstall = await PromptVersionAsync(plugin, cancellationToken);
                if (versionToInstall is null)
                {
                    LiveDisplayConsole.MarkupLine($"[yellow][{DisplayLabel(plugin).EscapeMarkup()}] 跳过[/]");
                    continue;
                }
                if (string.IsNullOrEmpty(versionToInstall.DownloadUrl))
                {
                    LiveDisplayConsole.MarkupLine($"[yellow][{DisplayLabel(plugin).EscapeMarkup()}] 缺少 DownloadUrl，跳过[/]");
                    continue;
                }
                try
                {
                    LiveDisplayConsole.WriteLine($"[{DisplayLabel(plugin)} v{versionToInstall.Version}] 正在下载");
                    using var response = await ResourceUpdater.HttpClient.GetAsync(versionToInstall.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var archive = new ZipArchive(stream);
                    LiveDisplayConsole.WriteLine($"[{DisplayLabel(plugin)}] 正在解压");
                    archive.ExtractToDirectory("./", true);
                    LiveDisplayConsole.WriteLine($"[{DisplayLabel(plugin)}] 安装完成");
                    installed++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    LiveDisplayConsole.MarkupLine($"[red][{DisplayLabel(plugin).EscapeMarkup()}] 安装失败:[/] {ex.Message.EscapeMarkup()}");
                }
            }
            return installed;
        }
    }
}
