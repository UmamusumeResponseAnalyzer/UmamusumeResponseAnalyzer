using System.Globalization;
using System.Net;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using i18n = UmamusumeResponseAnalyzer.Localization.Config;

namespace UmamusumeResponseAnalyzer
{
    public static class Config
    {
        internal const string CONFIG_FILEPATH = "config.yaml";
        private static YamlConfig Current { get; set; }
        private readonly static ISerializer _serializer = new SerializerBuilder().WithQuotingNecessaryStrings().WithNamingConvention(HyphenatedNamingConvention.Instance).Build();
        private readonly static IDeserializer _deserializer = new DeserializerBuilder()
            .WithDuplicateKeyChecking()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .Build();
        public static CoreConfig Core => Current.Core;
        public static RepositoryConfig Repository => Current.Repository;
        public static PluginConfig Plugin => Current.Plugin;
        public static UpdaterConfig Updater => Current.Updater;
        public static LanguageConfig Language => Current.Language;
        public static MiscConfig Misc => Current.Misc;
        internal static List<string> WorkspaceTaskbarTitleOrder
        {
            get => Current.WorkspaceTaskbarTitleOrder;
            set => Current.WorkspaceTaskbarTitleOrder = value;
        }

        internal static void Initialize()
        {
            if (File.Exists(CONFIG_FILEPATH))
            {
                Current = Deserialize(File.ReadAllText(CONFIG_FILEPATH), CONFIG_FILEPATH);
                UmamusumeResponseAnalyzer.ApplyCultureInfo();
            }
            else
            {
                Current = new();
                Save();
                // 首次运行也要应用 culture,否则首启菜单会用 OS 区域(如繁中系统→无对应资源→回退英文)。
                UmamusumeResponseAnalyzer.ApplyCultureInfo();
            }
        }

        public static void Save() =>
            File.WriteAllText(CONFIG_FILEPATH, Serialize(Current));

        internal static string Serialize(YamlConfig config) => _serializer.Serialize(config);

        internal static YamlConfig Deserialize(string yaml, string sourcePath)
        {
            try
            {
                var config = _deserializer.Deserialize<YamlConfigDto>(yaml)
                    ?? throw Invalid(sourcePath, "$", "内容不能为 null");
                return config.ToDomain(sourcePath);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (YamlException exception)
            {
                var detail = exception.InnerException?.Message ?? exception.Message;
                throw new InvalidDataException(
                    $"配置文件“{sourcePath}”不符合当前 schema（{exception.Start.Line}:{exception.Start.Column}）：{detail}",
                    exception);
            }
        }

        internal static InvalidDataException Invalid(string sourcePath, string fieldPath, string reason) =>
            new($"配置文件“{sourcePath}”中的 {fieldPath} {reason}。");

        internal static async Task PromptAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    var selected = ModalDialogs.Menu(
                        i18n.Settings_Title,
                        new[]
                        {
                            i18n.Tabs_Core_Title,
                            i18n.Tabs_Repository_Title,
                            i18n.Tabs_Plugin_Title,
                            i18n.Tabs_Updater_Title,
                            i18n.Tabs_Language_Title,
                            i18n.Tabs_Misc_Title,
                            i18n.Return
                        },
                        cancellationToken: cancellationToken);
                    if (selected == i18n.Return)
                        return;

                    if (selected == i18n.Tabs_Core_Title)
                        Core.Prompt(cancellationToken);
                    else if (selected == i18n.Tabs_Repository_Title)
                        Repository.Prompt(cancellationToken);
                    else if (selected == i18n.Tabs_Plugin_Title)
                        await Plugin.PromptAsync(cancellationToken);
                    else if (selected == i18n.Tabs_Updater_Title)
                        Updater.Prompt(cancellationToken);
                    else if (selected == i18n.Tabs_Language_Title)
                        Language.Prompt(cancellationToken);
                    else if (selected == i18n.Tabs_Misc_Title)
                        Misc.Prompt(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

    }

    internal sealed class YamlConfigDto
    {
        public CoreConfigDto? Core { get; set; }
        public RepositoryConfigDto? Repository { get; set; }
        public PluginConfigDto? Plugin { get; set; }
        public UpdaterConfigDto? Updater { get; set; }
        public LanguageConfigDto? Language { get; set; }
        public MiscConfigDto? Misc { get; set; }
        public List<string?>? WorkspaceTaskbarTitleOrder { get; set; } = [];

        internal YamlConfig ToDomain(string sourcePath) => new()
        {
            Core = RequiredSection(Core, sourcePath, "core").ToDomain(sourcePath),
            Repository = RequiredSection(Repository, sourcePath, "repository").ToDomain(sourcePath),
            Plugin = RequiredSection(Plugin, sourcePath, "plugin").ToDomain(),
            Updater = RequiredSection(Updater, sourcePath, "updater").ToDomain(sourcePath),
            Language = RequiredSection(Language, sourcePath, "language").ToDomain(sourcePath),
            Misc = RequiredSection(Misc, sourcePath, "misc").ToDomain(sourcePath),
            WorkspaceTaskbarTitleOrder = RequiredStrings(
                WorkspaceTaskbarTitleOrder,
                sourcePath,
                "workspace-taskbar-title-order")
        };

        private static T RequiredSection<T>(T? value, string sourcePath, string fieldPath) where T : class =>
            value ?? throw Config.Invalid(sourcePath, fieldPath, "不能为空或缺失");

        internal static T Required<T>(T? value, string sourcePath, string fieldPath) where T : class =>
            value ?? throw Config.Invalid(sourcePath, fieldPath, "不能为空");

        internal static T Required<T>(T? value, string sourcePath, string fieldPath) where T : struct =>
            value ?? throw Config.Invalid(sourcePath, fieldPath, "不能为空");

        internal static List<string> RequiredStrings(
            List<string?>? values,
            string sourcePath,
            string fieldPath)
        {
            if (values is null)
                throw Config.Invalid(sourcePath, fieldPath, "不能为空");
            return values
                .Select((value, index) =>
                    value ?? throw Config.Invalid(sourcePath, $"{fieldPath}[{index}]", "不能为空"))
                .ToList();
        }
    }

    internal sealed class CoreConfigDto
    {
        public string? ListenAddress { get; set; } = "127.0.0.1";
        public int? ListenPort { get; set; } = 4693;
        public bool? ShowFirstRunPrompt { get; set; } = true;

        internal CoreConfig ToDomain(string sourcePath) => new()
        {
            ListenAddress = YamlConfigDto.Required(ListenAddress, sourcePath, "core.listen-address"),
            ListenPort = YamlConfigDto.Required(ListenPort, sourcePath, "core.listen-port"),
            ShowFirstRunPrompt = YamlConfigDto.Required(
                ShowFirstRunPrompt,
                sourcePath,
                "core.show-first-run-prompt")
        };
    }

    internal sealed class RepositoryConfigDto
    {
        public List<string?>? Targets { get; set; } = [];

        internal RepositoryConfig ToDomain(string sourcePath) => new()
        {
            Targets = YamlConfigDto.RequiredStrings(Targets, sourcePath, "repository.targets")
        };
    }

    internal sealed class PluginConfigDto
    {
        internal PluginConfig ToDomain() => new();
    }

    internal sealed class UpdaterConfigDto
    {
        public bool? IsGithubBlocked { get; set; } =
            RegionInfo.CurrentRegion.Name == "CN" || CultureInfo.CurrentUICulture.Name == "zh-CN";
        public bool? TrainerIsMale { get; set; } = true;
        public string? DatabaseLanguage { get; set; } = "ja-JP";
        public string? CustomDatabaseRepository { get; set; } = string.Empty;
        public bool? ForceUseGithubToUpdate { get; set; } = false;

        internal UpdaterConfig ToDomain(string sourcePath) => new()
        {
            IsGithubBlocked = YamlConfigDto.Required(
                IsGithubBlocked,
                sourcePath,
                "updater.is-github-blocked"),
            TrainerIsMale = YamlConfigDto.Required(
                TrainerIsMale,
                sourcePath,
                "updater.trainer-is-male"),
            DatabaseLanguage = YamlConfigDto.Required(
                DatabaseLanguage,
                sourcePath,
                "updater.database-language"),
            CustomDatabaseRepository = CustomDatabaseRepository ?? string.Empty,
            ForceUseGithubToUpdate = YamlConfigDto.Required(
                ForceUseGithubToUpdate,
                sourcePath,
                "updater.force-use-github-to-update")
        };
    }

    internal sealed class LanguageConfigDto
    {
        public LanguageConfig.Language? Selected { get; set; } = LanguageConfig.Language.AutoDetect;

        internal LanguageConfig ToDomain(string sourcePath) => new()
        {
            Selected = YamlConfigDto.Required(Selected, sourcePath, "language.selected")
        };
    }

    internal sealed class MiscConfigDto
    {
        public bool? SaveResponseForDebug { get; set; } = false;

        internal MiscConfig ToDomain(string sourcePath) => new()
        {
            SaveResponseForDebug = YamlConfigDto.Required(
                SaveResponseForDebug,
                sourcePath,
                "misc.save-response-for-debug")
        };
    }

    public class YamlConfig
    {
        public CoreConfig Core { get; set; } = new();
        public RepositoryConfig Repository { get; set; } = new();
        public PluginConfig Plugin { get; set; } = new();
        public UpdaterConfig Updater { get; set; } = new();
        public LanguageConfig Language { get; set; } = new();
        public MiscConfig Misc { get; set; } = new();
        public List<string> WorkspaceTaskbarTitleOrder { get; set; } = [];
    }

    #region class
    public class CoreConfig
    {
        public string ListenAddress { get; set; } = "127.0.0.1";
        public int ListenPort { get; set; } = 4693;
        public bool ShowFirstRunPrompt { get; set; } = true;

        internal void Prompt(CancellationToken cancellationToken)
        {
            var firstRunTitle = i18n.ResourceManager.GetString(
                    "Tabs_Core_ShowFirstRunPrompt",
                    i18n.Culture)
                ?? nameof(ShowFirstRunPrompt);
            while (true)
            {
                var addressItem = $"{i18n.Tabs_Core_ListenAddress}: {ListenAddress}";
                var portItem = $"{i18n.Tabs_Core_ListenPort}: {ListenPort}";
                var firstRunItem = $"{firstRunTitle}: {ShowFirstRunPrompt}";
                var selected = ModalDialogs.Menu(
                    i18n.Tabs_Core_Title,
                    new[] { addressItem, portItem, firstRunItem, i18n.Return },
                    cancellationToken: cancellationToken);
                if (selected == i18n.Return)
                    return;

                if (selected == addressItem)
                {
                    while (true)
                    {
                        var address = ModalDialogs.Ask(
                            i18n.Tabs_Core_ListenAddressPrompt,
                            ListenAddress,
                            cancellationToken: cancellationToken);
                        if (!IPAddress.TryParse(address, out _))
                            continue;
                        ListenAddress = address;
                        break;
                    }
                }
                else if (selected == portItem)
                {
                    while (true)
                    {
                        var port = ModalDialogs.Ask(
                            i18n.Tabs_Core_ListenPortPrompt,
                            ListenPort.ToString(),
                            cancellationToken: cancellationToken);
                        if (!int.TryParse(port, out var parsed))
                            continue;
                        ListenPort = parsed;
                        break;
                    }
                }
                else if (selected == firstRunItem)
                {
                    ShowFirstRunPrompt = !ShowFirstRunPrompt;
                }
                Config.Save();
            }
        }
    }

    public class RepositoryConfig
    {
        public List<string> Targets { get; set; } = [];

        internal void Prompt(CancellationToken cancellationToken)
        {
            while (true)
            {
                var targetsItem = $"{i18n.Tabs_Repository_Targets}: {string.Join(',', Targets)}";
                var selected = ModalDialogs.Menu(
                    i18n.Tabs_Repository_Title,
                    new[] { targetsItem, i18n.Return },
                    cancellationToken: cancellationToken);
                if (selected == i18n.Return)
                    return;

                var input = ModalDialogs.Ask(
                    i18n.Tabs_Repository_TargetsPrompt,
                    string.Join(',', Targets),
                    allowEmpty: true,
                    cancellationToken: cancellationToken);
                Targets = string.IsNullOrEmpty(input)
                    ? []
                    : [.. input.Replace('，', ',').Split(',')];
                Config.Save();
            }
        }
    }

    public class PluginConfig
    {
        internal async Task PromptAsync(CancellationToken cancellationToken)
        {
            var plugins = BuildPluginChoices(
                PluginManager.SnapshotPluginStatuses().Where(plugin => plugin.IsLoaded));
            var choices = plugins
                .Select(x => (Label: x.Key, InternalName: (string?)x.Value))
                .Append((i18n.Return, null))
                .ToArray();
            while (true)
            {
                var selected = ModalDialogs.Menu(
                    i18n.Tabs_Plugin_Title,
                    choices,
                    x => x.Label,
                    cancellationToken: cancellationToken);
                if (selected.InternalName is null)
                    return;
                var plugin = PluginManager.FindLoadedPlugin(selected.InternalName)
                    ?? throw new InvalidOperationException($"插件已卸载，无法打开设置: {selected.InternalName}");
                await PluginConfigPrompt.RunAsync(plugin, cancellationToken);
            }
        }

        internal static SortedDictionary<string, string> BuildPluginChoices(
            IEnumerable<PluginManager.PluginRuntimeStatus> plugins)
        {
            var list = plugins.ToList();
            var duplicateNames = list
                .GroupBy(x => x.DisplayName, StringComparer.Ordinal)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToHashSet(StringComparer.Ordinal);

            var choices = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var plugin in list)
            {
                var baseLabel = duplicateNames.Contains(plugin.DisplayName)
                    ? $"{plugin.DisplayName} ({plugin.Author}/{plugin.InternalName})"
                    : plugin.DisplayName;

                var label = baseLabel;
                for (var suffix = 2; choices.ContainsKey(label); suffix++)
                    label = $"{baseLabel} #{suffix}";

                choices.Add(label, plugin.InternalName);
            }
            return choices;
        }
    }

    public class UpdaterConfig
    {
        public bool IsGithubBlocked { get; set; } = RegionInfo.CurrentRegion.Name == "CN" || CultureInfo.CurrentUICulture.Name == "zh-CN";
        public bool TrainerIsMale { get; set; } = true;
        public string DatabaseLanguage { get; set; } = "ja-JP";
        public string CustomDatabaseRepository { get; set; } = string.Empty;
        public bool ForceUseGithubToUpdate { get; set; }

        internal void Prompt(CancellationToken cancellationToken)
        {
            string Label(string property) =>
                i18n.ResourceManager.GetString($"Tabs_Updater_{property}", i18n.Culture)
                ?? property;

            while (true)
            {
                var trainerItem = $"{Label(nameof(TrainerIsMale))}: {TrainerIsMale}";
                var languageItem = $"{Label(nameof(DatabaseLanguage))}: {DatabaseLanguage}";
                var repositoryItem =
                    $"{Label(nameof(CustomDatabaseRepository))}: {CustomDatabaseRepository}";
                var forceGithubItem =
                    $"{i18n.Tabs_Updater_ForceUseGithubToUpdate}: {ForceUseGithubToUpdate}";
                var selected = ModalDialogs.Menu(
                    i18n.Tabs_Updater_Title,
                    new[]
                    {
                        trainerItem,
                        languageItem,
                        repositoryItem,
                        forceGithubItem,
                        i18n.Return
                    },
                    cancellationToken: cancellationToken);
                if (selected == i18n.Return)
                    return;

                if (selected == trainerItem)
                {
                    TrainerIsMale = !TrainerIsMale;
                }
                else if (selected == languageItem)
                {
                    DatabaseLanguage = ModalDialogs.Menu(
                        nameof(DatabaseLanguage),
                        new[] { "ja-JP", "zh-TW", "zh-CN" },
                        cancellationToken: cancellationToken);
                }
                else if (selected == repositoryItem)
                {
                    while (true)
                    {
                        var url = ModalDialogs.Ask(
                            i18n.Tabs_Updater_CustomDatabaseRepositoryPrompt,
                            CustomDatabaseRepository,
                            allowEmpty: true,
                            cancellationToken: cancellationToken);
                        if (!string.IsNullOrEmpty(url) &&
                            !Uri.TryCreate(url, UriKind.Absolute, out _))
                            continue;
                        CustomDatabaseRepository = url;
                        break;
                    }
                }
                else if (selected == forceGithubItem)
                {
                    ForceUseGithubToUpdate = !ForceUseGithubToUpdate;
                }
                Config.Save();
            }
        }
    }

    public class LanguageConfig
    {
        public Language Selected { get; internal set; } = Language.AutoDetect;

        internal void Prompt(CancellationToken cancellationToken)
        {
            var choices = Enum.GetValues<Language>()
                .ToDictionary(
                    language => i18n.ResourceManager.GetString(
                            $"Tabs_Language_{language}",
                            i18n.Culture)
                        ?? language.ToString());
            var selected = ModalDialogs.Menu(
                i18n.Tabs_Language_Title,
                choices.Keys,
                cancellationToken: cancellationToken);
            Selected = choices[selected];
            Config.Save();
            UmamusumeResponseAnalyzer.Restart();
        }

        public static string GetCulture()
        {
            return Config.Language.Selected switch
            {
                Language.SimplifiedChinese => "zh-CN",
                Language.Japanese => "ja-JP",
                Language.English => "en-US",
                _ => AutoDetectCulture(Thread.CurrentThread.CurrentCulture.Name),
            };
        }

        // AutoDetect:把 OS 区域映射到最接近的「已提供 UI 资源」的语言。
        // 只有 zh-CN/ja-JP/en-US(+invariant 英文)有 .resx;繁中(zh-TW)/zh-HK 等没有对应资源,
        // 未提供资源的 OS 区域名会让 ResourceManager 回退 invariant 英文,导致繁中系统下整个 UI 变英文。
        // 这里把所有 zh-* 归到 zh-CN(目前唯一的中文 UI 资源),其余按语言主标签归类,未知归 en-US。
        internal static string AutoDetectCulture(string osCultureName) =>
            osCultureName.Split('-')[0] switch
            {
                "zh" => "zh-CN",
                "ja" => "ja-JP",
                "en" => "en-US",
                _ => "en-US",
            };

        public enum Language
        {
            AutoDetect,
            SimplifiedChinese,
            Japanese,
            English
        }
    }

    public class MiscConfig
    {
        public bool SaveResponseForDebug { get; set; }
        public void Prompt(CancellationToken cancellationToken)
        {
            var label = i18n.ResourceManager.GetString(
                    $"Tabs_Debug_{nameof(SaveResponseForDebug)}",
                    i18n.Culture)
                ?? nameof(SaveResponseForDebug);
            var selected = ModalDialogs.MultiSelect(
                i18n.Tabs_Debug_Title,
                [label],
                SaveResponseForDebug ? [label] : [],
                cancellationToken: cancellationToken);
            SaveResponseForDebug = selected.Contains(label);
            Config.Save();
        }
    }
    #endregion
}
