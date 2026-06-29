using Newtonsoft.Json;
using Spectre.Console;
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
        // URACloud 插件 API——唯一的插件源，不可由用户配置。正式域名经 NGINX 反代：root 是前端、/api 才是后端，
        // 故插件源在 /api/Plugins（生产/测试统一用）。本地起 URACloud 无反代时直连后端 http://localhost:4694/Plugins。
        const string PluginApiBase = "https://ura.shuise.net/api/Plugins";
        static readonly Version ZeroVersion = new(0, 0, 0);

        public static async Task ShowMenuAsync()
        {
            await LiveDisplayConsole.RunAsync(ShowMenuCoreAsync);
        }

        static async Task ShowMenuCoreAsync()
        {
            var plugins = await FetchAllPluginsAsync();
            if (plugins.Count == 0)
            {
                // 不要在这之前 Clear():FetchAsync 失败时打印的红色原因(URL + 异常)要留在屏上,
                // 否则只剩这句泛泛的"没有拉到",用户无从判断是网络/端点/过滤问题。
                LiveDisplayConsole.MarkupLine("[yellow]没有从插件仓库拉到插件信息。[/]");
                LiveDisplayConsole.MarkupLine("按任意键返回");
                LiveDisplayConsole.ReadKey(intercept: true);
                return;
            }
            LiveDisplayConsole.Clear();

            var pluginSelection = new MultiSelectionPrompt<PluginInformation>()
                .Title("选择要安装的插件")
                .WrapAround(true)
                .UseConverter(FormatChoice)
                // 取终端可视高度:长列表在 Spectre 视口内滚动,避免溢出把顶部条目挤出屏幕、光标够不着。
                .PageSize(Math.Clamp(Console.WindowHeight - 6, 10, 30))
                .NotRequired();

            pluginSelection.AddChoices(plugins
                .OrderBy(p => CategoryOrder(string.IsNullOrWhiteSpace(p.Category) ? UncategorizedLabel : p.Category))
                .ThenBy(p => string.IsNullOrWhiteSpace(p.Category) ? UncategorizedLabel : p.Category)
                .ThenBy(DisplayLabel, StringComparer.OrdinalIgnoreCase));

            var selectedPlugins = LiveDisplayConsole.Prompt(pluginSelection);
            if (selectedPlugins.Count == 0)
            {
                LiveDisplayConsole.Clear();
                return;
            }

            // 同一 InternalName 的多个 fork(不同作者)都能勾选,但本地磁盘/加载器只认 InternalName——
            // 安装会按程序集名互相覆盖、实际只落一个。检出冲突,让用户每个 InternalName 只保留一个来源。
            selectedPlugins = DedupeForks(selectedPlugins);
            if (selectedPlugins.Count == 0)
            {
                LiveDisplayConsole.Clear();
                return;
            }

            ResolveDependencies(selectedPlugins, plugins);
            var installed = await InstallPluginsAsync(selectedPlugins);

            if (installed.Count > 0)
            {
                LiveDisplayConsole.WriteLine("正在应用插件（热重载，免重启）……");
                var needRestart = PluginManager.ReloadPlugins([.. installed]);
                if (needRestart.Count == 0)
                {
                    LiveDisplayConsole.MarkupLine("[green]插件已安装并生效。[/]");
                }
                else
                {
                    // 极少数无法热重载的情形（如 [LoadInHostContext] 插件）回退到重启，绝不比旧行为差
                    LiveDisplayConsole.MarkupLine($"[yellow]{string.Join("、", needRestart).EscapeMarkup()} 需重启才能生效，按任意键重启。[/]");
                    LiveDisplayConsole.ReadKey();
                    global::UmamusumeResponseAnalyzer.UmamusumeResponseAnalyzer.Restart();
                }
            }
        }

        public static async Task<IReadOnlyList<PluginUpdateInfo>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            var loaded = PluginManager.SnapshotLoadedPlugins();
            if (loaded.Count == 0) return [];
            var remote = await FetchAllPluginsAsync(silent: true, cancellationToken);
            if (remote.Count == 0) return [];

            var remoteByInternalName = remote
                .GroupBy(r => r.InternalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var updates = new List<PluginUpdateInfo>();
            foreach (var plugin in loaded)
            {
                var assemblyName = PluginManager.InternalName(plugin);
                // 同名 fork 用作者消歧：只有一个匹配时直接用（与旧行为一致、不退化）；多个 fork 时
                // 按已加载插件的 Author 选对应那个，选不出就跳过（宁可不提示，也不对错误的 fork 误报更新）。
                if (!remoteByInternalName.TryGetValue(assemblyName, out var matches)) continue;
                var remoteInfo = matches.Count switch
                {
                    1 => matches[0],
                    _ => matches.FirstOrDefault(r => string.Equals(r.Author, plugin.Author, StringComparison.OrdinalIgnoreCase)),
                };
                if (remoteInfo is null) continue;
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

        static async Task<List<PluginInformation>> FetchAllPluginsAsync(bool silent = false, CancellationToken cancellationToken = default)
        {
            if (!silent) LiveDisplayConsole.WriteLine("正在从插件仓库获取插件信息");
            var list = await FetchAsync(PluginApiBase, cancellationToken, silent);
            return BuildCatalog(list, Config.Repository.Targets, PluginApiBase);
        }

        // 把原始 manifest 列表过滤成目录。**不按 InternalName 去重**：仓库允许同名不同作者的
        // fork（如 离披/StatisticsCollector 与 URACloud-Tester/StatisticsCollector），插件身份
        // 是 (Author, InternalName) 复合键。早先这里用 Dictionary 拿 InternalName 当 key，后到
        // 的 fork 会覆盖先到的、连分类一起被吞掉（症状：目录里只剩一个、其余“消失”）。提成纯函数便于回归测试。
        internal static List<PluginInformation> BuildCatalog(
            IEnumerable<PluginInformation> raw, IReadOnlyCollection<string> targetFilter, string apiBase)
        {
            var noTargetFilter = targetFilter.Count == 0;
            var plugins = new List<PluginInformation>();
            foreach (var plugin in raw.Where(x =>
                x is { InternalName.Length: > 0, Author.Length: > 0 }
                && (noTargetFilter || x.Targets is not { Length: > 0 } || x.Targets.Intersect(targetFilter).Any())))
            {
                plugin.DownloadUrl = $"{apiBase}/{Uri.EscapeDataString(plugin.Author)}/{Uri.EscapeDataString(plugin.InternalName)}/versions/{Uri.EscapeDataString(plugin.RawVersion)}/download";
                plugins.Add(plugin);
            }
            return plugins;
        }

        static async Task<List<PluginInformation>> FetchAsync(string url, CancellationToken cancellationToken, bool silent = false)
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
                if (!silent)
                    LiveDisplayConsole.MarkupLine($"[red]从 {url.EscapeMarkup()} 拉取仓库失败:[/] {ex.Message.EscapeMarkup()}");
                return [];
            }
        }

        // 空分类的兜底组名。用 "其他" 而非单独的 "未分类"，好让它与 manifest 里
        // 真实声明 Category="其他" 的插件合并进同一组（与前端 DEFAULT_CATEGORY 一致）。
        const string UncategorizedLabel = "其他";
        // "其他" 永远排在所有具体分类之后。
        static int CategoryOrder(string category) => category == UncategorizedLabel ? 1 : 0;

        static string DisplayLabel(PluginInformation info) =>
            string.IsNullOrWhiteSpace(info.DisplayName) ? info.InternalName : info.DisplayName;

        static string FormatChoice(PluginInformation info)
        {
            var version = info.Version == ZeroVersion ? "" : $" v{info.Version}";
            // @作者:区分同名不同作者的 fork；真实选择值直接绑定 PluginInformation,显示文本不参与身份判断。
            var author = string.IsNullOrWhiteSpace(info.Author) ? "" : $" @{info.Author}";
            var desc = string.IsNullOrEmpty(info.Description) ? "" : $" — {info.Description}";
            // 选项必须单行:换行折成空格、再按终端列宽截断。Spectre 的 SelectionPrompt 只要有一项折行,
            // 视口行数就算错,顶部条目会被挤出屏幕、光标够不着(本次 bug 现场)。先拼纯文本 → 截断 →
            // 整体 EscapeMarkup:既避免截断切断 markup 标签,也顺手堵掉名字/描述含 '[' 的渲染坑。
            var raw = $"{DisplayLabel(info)}{version}{author}{desc}".ReplaceLineEndings(" ");
            return TruncateToWidth(raw, Math.Max(30, Console.WindowWidth - 12)).EscapeMarkup();
        }

        // 估算字符的等宽控制台列宽:CJK/全角占 2 列,其余 1 列(够用的近似,不追求 Unicode 精确)。
        internal static int CharWidth(char c) =>
            c >= 'ᄀ' && (
                c <= 'ᅟ'                        // Hangul Jamo
                || (c >= '⺀' && c <= '꓏')  // CJK 部首/汉字/假名/注音…
                || (c >= '가' && c <= '힣')  // Hangul 音节
                || (c >= '豈' && c <= '﫿')    // CJK 兼容汉字
                || (c >= '＀' && c <= '｠')  // 全角 ASCII
                || (c >= '￠' && c <= '￦')) // 全角符号
            ? 2 : 1;

        // 按显示列宽把字符串截成单行,超出补 “…”。
        internal static string TruncateToWidth(string s, int maxWidth)
        {
            var width = 0;
            var ellipsisStart = 0;
            for (var i = 0; i < s.Length; i++)
            {
                var next = width + CharWidth(s[i]);
                if (next <= maxWidth - 1)
                    ellipsisStart = i + 1;
                if (next > maxWidth)
                    return s[..ellipsisStart] + "…";
                width = next;
            }
            return s;
        }

        internal static void ResolveDependencies(List<PluginInformation> selectedPlugins, List<PluginInformation> catalog)
        {
            var selectedByName = selectedPlugins.ToDictionary(p => p.InternalName, StringComparer.OrdinalIgnoreCase);
            var catalogByName = catalog
                .GroupBy(p => p.InternalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < selectedPlugins.Count; i++)
            {
                foreach (var dependency in selectedPlugins[i].Dependencies)
                {
                    if (selectedByName.ContainsKey(dependency)) continue;

                    // 依赖只按 InternalName 声明（数据模型里不含作者），同名 fork 无法区分——取第一个
                    // 提供该 InternalName 的即可，符合既有“依赖单一来源”的假设。
                    if (catalogByName.TryGetValue(dependency, out var dependencyPluginInfo))
                    {
                        selectedPlugins.Add(dependencyPluginInfo);
                        selectedByName[dependencyPluginInfo.InternalName] = dependencyPluginInfo;
                    }
                    else
                    {
                        LiveDisplayConsole.WriteLine($"没有在插件仓库中找到依赖项{dependency}");
                    }
                }
            }
        }

        /// <summary>
        /// 同一 InternalName 被勾选了多个 fork(不同作者)时,逐个让用户二选一——本地磁盘/加载器只按
        /// 程序集名(==InternalName)落地,装多个会互相覆盖,UI 的多选无法兑现。每个 InternalName 只留一个。
        /// </summary>
        static List<PluginInformation> DedupeForks(List<PluginInformation> selected)
        {
            var result = new List<PluginInformation>();
            foreach (var group in selected.GroupBy(p => p.InternalName, StringComparer.OrdinalIgnoreCase))
            {
                var forks = group.ToList();
                if (forks.Count == 1)
                {
                    result.Add(forks[0]);
                    continue;
                }
                LiveDisplayConsole.MarkupLine($"[yellow]插件 {group.Key.EscapeMarkup()} 选中了多个来源,本地只能安装一个,请选择保留哪个:[/]");
                var labelToFork = new Dictionary<string, PluginInformation>(StringComparer.Ordinal);
                foreach (var f in forks)
                    labelToFork[$"{DisplayLabel(f).EscapeMarkup()} @{f.Author.EscapeMarkup()}"] = f;
                var pick = LiveDisplayConsole.Prompt(new SelectionPrompt<string>()
                    .Title($"为 [green]{group.Key.EscapeMarkup()}[/] 选择来源")
                    .WrapAround(true)
                    .AddChoices(labelToFork.Keys));
                result.Add(labelToFork[pick]);
            }
            return result;
        }

        static string VersionsUrl(PluginInformation plugin) =>
            $"{PluginApiBase}/{Uri.EscapeDataString(plugin.Author)}/{Uri.EscapeDataString(plugin.InternalName)}/versions";

        static async Task<PluginInformation?> PromptVersionAsync(PluginInformation plugin, CancellationToken cancellationToken)
        {
            var versions = await FetchAsync(VersionsUrl(plugin), cancellationToken);
            // 版本 manifest 不带 DownloadUrl——用原始 plugin 的 Author/InternalName 拼，不信任 response 字段
            foreach (var v in versions)
                v.DownloadUrl = $"{PluginApiBase}/{Uri.EscapeDataString(plugin.Author)}/{Uri.EscapeDataString(plugin.InternalName)}/versions/{Uri.EscapeDataString(v.RawVersion)}/download";
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

        // 安装落地路径:Plugins/{InternalName}.zip。SDK 打的 zip 内容在根、无 Plugins/ 前缀,宿主 ScanAll 扫的就是 Plugins/*.zip。
        // (历史回归:URACloud 迁移期曾 ExtractToDirectory("./") 解到 WORKING_DIRECTORY 根 → ScanAll 扫不到 → 装了等于没装。)
        internal static string InstallZipPath(string internalName) => Path.Combine("Plugins", $"{internalName}.zip");

        // 把插件名当“字面方括号标签”塞进 Spectre markup:外层必须用 [[ ]](字面括号),不能用 [ ](会被当成样式/颜色解析)。
        // 历史崩溃:安装信息曾写 [{name}],而 EscapeMarkup 只转义名字【内部】的括号;非样式名(如 CJK「梦想杯剧本解析器」)
        // 被 Spectre 当样式解析 → InvalidOperationException "Could not find color or style" → 未捕获 → 崩掉整个程序。
        internal static string Bracketed(string label) => $"[[{label.EscapeMarkup()}]]";

        internal static async Task<List<string>> InstallPluginsAsync(List<PluginInformation> selectedPlugins, CancellationToken cancellationToken = default)
        {
            var installed = new List<string>();
            var conflictingNames = selectedPlugins
                .GroupBy(p => p.InternalName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(p => p.Author).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in selectedPlugins)
            {
                if (conflictingNames.Contains(plugin.InternalName))
                {
                    LiveDisplayConsole.MarkupLine($"[yellow]{Bracketed(plugin.InternalName)} 被多个作者同时选中，跳过；请一次只安装其中一个 fork[/]");
                    continue;
                }

                var installedFork = PluginManager.SnapshotLoadedPlugins()
                    .FirstOrDefault(p => string.Equals(PluginManager.InternalName(p), plugin.InternalName, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(p.Author, plugin.Author, StringComparison.OrdinalIgnoreCase));
                if (installedFork != null)
                {
                    LiveDisplayConsole.MarkupLine($"[yellow]{Bracketed(DisplayLabel(plugin))} 与已安装的 {installedFork.Author.EscapeMarkup()}/{plugin.InternalName.EscapeMarkup()} 冲突，跳过[/]");
                    continue;
                }

                var versionToInstall = await PromptVersionAsync(plugin, cancellationToken);
                if (versionToInstall is null)
                {
                    LiveDisplayConsole.MarkupLine($"[yellow]{Bracketed(DisplayLabel(plugin))} 跳过[/]");
                    continue;
                }
                if (string.IsNullOrEmpty(versionToInstall.DownloadUrl))
                {
                    LiveDisplayConsole.MarkupLine($"[yellow]{Bracketed(DisplayLabel(plugin))} 缺少 DownloadUrl，跳过[/]");
                    continue;
                }
                try
                {
                    LiveDisplayConsole.WriteLine($"[{DisplayLabel(plugin)} v{versionToInstall.Version}] 正在下载");
                    // 直接把下载的 zip 落成 Plugins/{InternalName}.zip,不解压(SDK 打的 zip 内容在根、无 Plugins/ 前缀;
                    // 宿主 ScanAll 扫的就是 Plugins/*.zip,与本地开发 deploy 一致,热重载重扫即可发现)。
                    await DownloadPluginZipAsync(versionToInstall.DownloadUrl, plugin.InternalName, cancellationToken);
                    LiveDisplayConsole.WriteLine($"[{DisplayLabel(plugin)}] 安装完成");
                    installed.Add(plugin.InternalName);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    LiveDisplayConsole.MarkupLine($"[red]{Bracketed(DisplayLabel(plugin))} 安装失败:[/] {ex.Message.EscapeMarkup()}");
                }
            }
            return installed;
        }

        internal static async Task DownloadPluginZipAsync(string url, string internalName, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory("Plugins");
            var tempDir = Path.Combine(Path.GetTempPath(), "UmamusumeResponseAnalyzer");
            Directory.CreateDirectory(tempDir);
            foreach (var stale in Directory.GetFiles(tempDir, "plugin-*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(stale) < DateTime.UtcNow.AddDays(-1))
                    File.Delete(stale);
            }

            using var response = await ResourceUpdater.HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var dest = InstallZipPath(internalName);
            var tempPath = Path.Combine(tempDir, $"plugin-{internalName}-{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await response.Content.CopyToAsync(fs, cancellationToken);
                    await fs.FlushAsync(cancellationToken);
                }
                File.Move(tempPath, dest, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        /// <summary>
        /// 非交互安装:按 (author, internalName, version) 从【硬编码的】URACloud 仓库下载并落盘
        /// Plugins/{internalName}.zip。供 :4693 的 Web 端点(<see cref="WebInstallApi"/>)调用——刻意
        /// 只收三段引用、<b>绝不接受任何 URL</b>,下载源恒为 <see cref="PluginApiBase"/>。因此伪造的网页
        /// 既改不了下载源也投不了毒,顶多触发安装一个仓库里真实存在的插件。调用方负责随后调
        /// <see cref="PluginManager.ReloadPlugins"/> 完成热重载。
        /// </summary>
        public static async Task InstallByReferenceAsync(string author, string internalName, string version, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(author) || !IsSafeSegment(internalName) || string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("非法的插件标识");
            var url = $"{PluginApiBase}/{Uri.EscapeDataString(author)}/{Uri.EscapeDataString(internalName)}/versions/{Uri.EscapeDataString(version)}/download";
            await DownloadPluginZipAsync(url, internalName, cancellationToken);
        }

        static bool IsSafeSegment(string s) =>
            !string.IsNullOrEmpty(s) && s.Length <= 100 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }
}
