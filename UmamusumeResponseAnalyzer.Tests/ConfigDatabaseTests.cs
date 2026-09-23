using System.Reflection;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("Database")]
    public class ConfigSerializationTests
    {
        [Theory]
        [InlineData("{}")]
        [InlineData("core: {}")]
        [InlineData("plugin: {}")]
        [InlineData("plugin: null")]
        [InlineData("updater: {}")]
        [InlineData("language: {}")]
        [InlineData("misc: {}")]
        public void Deserialize_MissingSectionsAndFields_UsesRuntimeDefaults(string yaml)
        {
            var config = Config.Deserialize(yaml, "settings/config.yaml");

            Assert.Equal(Config.Serialize(new YamlConfig()), Config.Serialize(config));
        }

        [Theory]
        [InlineData("zh-CN")]
        [InlineData("en-US")]
        [InlineData("ja-JP")]
        public void Deserialize_DefaultsFollowCurrentCulture(string culture)
        {
            var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
            var previousUiCulture = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new(culture);
                System.Globalization.CultureInfo.CurrentUICulture = new(culture);
                var config = Config.Deserialize("{}", "config.yaml");

                Assert.Equal(Config.Serialize(new YamlConfig()), Config.Serialize(config));
                Assert.Equal(
                    System.Globalization.RegionInfo.CurrentRegion.Name == "CN" || culture == "zh-CN",
                    config.Updater.IsGithubBlocked);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previousCulture;
                System.Globalization.CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("---\n")]
        [InlineData("null")]
        [InlineData("~")]
        public void Deserialize_NullDocument_ThrowsWithSourceAndRootPath(string yaml)
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                Config.Deserialize(yaml, "settings/config.yaml"));

            Assert.Contains("settings/config.yaml", exception.Message);
            Assert.Contains("$", exception.Message);
        }

        [Theory]
        [InlineData("core: null")]
        [InlineData("updater: null")]
        [InlineData("language: null")]
        [InlineData("misc: null")]
        [InlineData("core: {listen-address: null}")]
        [InlineData("core: {listen-port: null}")]
        [InlineData("core: {listen-port: ~}")]
        [InlineData("core: {listen-port: }")]
        [InlineData("core: {show-first-run-prompt: null}")]
        [InlineData("updater: {is-github-blocked: null}")]
        [InlineData("updater: {trainer-is-male: null}")]
        [InlineData("updater: {database-language: null}")]
        [InlineData("updater: {force-use-github-to-update: null}")]
        [InlineData("language: {selected: null}")]
        [InlineData("misc: {save-response-for-debug: null}")]
        [InlineData("workspace-taskbar-title-order: null")]
        [InlineData("updater: {custom-database-repository: &nil null}\ncore: {listen-port: *nil}")]
        [InlineData("updater: {custom-database-repository: &nil null}\ncore: {show-first-run-prompt: *nil}")]
        [InlineData("updater: {custom-database-repository: &nil null}\nlanguage: {selected: *nil}")]
        [InlineData("updater: {custom-database-repository: &nil null}\ncore: {listen-address: *nil}")]
        [InlineData("updater: {custom-database-repository: &nil null}\ncore: *nil")]
        public void Deserialize_ForbiddenNull_ThrowsWithSourceAndYamlPosition(string yaml)
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                Config.Deserialize(yaml, "settings/config.yaml"));
            var yamlException = Assert.IsAssignableFrom<YamlDotNet.Core.YamlException>(exception.InnerException);

            Assert.Contains("settings/config.yaml", exception.Message);
            Assert.Contains($"{yamlException.Start.Line}:{yamlException.Start.Column}", exception.Message);
            Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Deserialize_UnknownFields_AreIgnoredAndDiscardedWhenSaved()
        {
            var yaml = """
                removed-section:
                  duplicate: 1
                  duplicate: 2
                core:
                  listen-port: 5000
                  request-additional-header: true
                plugin:
                  removed-option: null
                """;
            var config = Config.Deserialize(yaml, "config.yaml");
            var saved = Config.Serialize(config);

            Assert.Equal(5000, config.Core.ListenPort);
            Assert.DoesNotContain("removed-", saved);
            Assert.DoesNotContain("duplicate", saved);
            Assert.DoesNotContain("request-additional-header", saved);
            Assert.DoesNotContain("plugin:", saved);
            Assert.Equal(saved, Config.Serialize(Config.Deserialize(saved, "saved.yaml")));
        }

        [Theory]
        [InlineData("core: {}\ncore: {}", "core")]
        [InlineData("core: {listen-port: 4693, listen-port: 5000}", "listen-port")]
        [InlineData("unknown: 1\nunknown: 2", "unknown")]
        [InlineData("core: {unknown: 1, unknown: 2}", "unknown")]
        public void Deserialize_DuplicateField_ThrowsWithSourceAndField(string yaml, string field)
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                Config.Deserialize(yaml, "config.yaml"));

            Assert.Contains("config.yaml", exception.Message);
            Assert.Contains(field, exception.Message);
            Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("core: {listen-port: nope}")]
        [InlineData("core: {show-first-run-prompt: nope}")]
        [InlineData("language: {selected: nope}")]
        public void Deserialize_InvalidScalar_ThrowsWithSourceAndYamlPosition(string yaml)
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                Config.Deserialize(yaml, "settings/config.yaml"));
            var yamlException = Assert.IsAssignableFrom<YamlDotNet.Core.YamlException>(exception.InnerException);

            Assert.Contains("settings/config.yaml", exception.Message);
            Assert.Contains($"{yamlException.Start.Line}:{yamlException.Start.Column}", exception.Message);
            Assert.Contains("nope", exception.Message);
        }

        [Theory]
        [InlineData("workspace-taskbar-title-order: [null]", "workspace-taskbar-title-order[0]")]
        [InlineData("updater: {custom-database-repository: &nil null}\nworkspace-taskbar-title-order: [first, *nil]", "workspace-taskbar-title-order[1]")]
        public void Deserialize_NullCollectionItem_ThrowsWithIndexedFieldPath(string yaml, string fieldPath)
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                Config.Deserialize(yaml, "config.yaml"));

            Assert.Contains("config.yaml", exception.Message);
            Assert.Contains(fieldPath, exception.Message);
        }

        [Theory]
        [InlineData("updater: {}")]
        [InlineData("updater: {custom-database-repository: null}")]
        [InlineData("updater: {custom-database-repository: }")]
        [InlineData("updater: {custom-database-repository: ''}")]
        public void Deserialize_OptionalCustomDatabaseRepository_NormalizesToEmpty(string yaml)
        {
            Assert.Equal(string.Empty, Config.Deserialize(yaml, "config.yaml").Updater.CustomDatabaseRepository);
        }

        [Fact]
        public void Deserialize_Aliases_PreserveValues()
        {
            var config = Config.Deserialize("""
                updater: {trainer-is-male: &enabled false}
                core: {show-first-run-prompt: *enabled}
                """, "config.yaml");

            Assert.False(config.Updater.TrainerIsMale);
            Assert.False(config.Core.ShowFirstRunPrompt);
        }

        [Theory]
        [InlineData("core: {listen-port: *missing}")]
        [InlineData("core: {listen-port: *nil}\nupdater: {custom-database-repository: &nil null}")]
        public void Deserialize_UndeclaredOrForwardAlias_PreservesYamlError(string yaml)
        {
            var exception = Assert.Throws<InvalidDataException>(() => Config.Deserialize(yaml, "config.yaml"));

            Assert.Contains("config.yaml", exception.Message);
            Assert.IsAssignableFrom<YamlDotNet.Core.YamlException>(exception.InnerException);
        }
        [Fact]
        public void Deserialize_MissingFields_UsesCurrentDefaults()
        {
            var yaml = """
                core: {listen-port: 4693, show-first-run-prompt: true}
                updater: {is-github-blocked: false, trainer-is-male: true, database-language: ja-JP, force-use-github-to-update: false}
                language: {selected: AutoDetect}
                misc: {save-response-for-debug: false}
                """;

            var config = Config.Deserialize(yaml, "config.yaml");

            Assert.Equal("127.0.0.1", config.Core.ListenAddress);
            Assert.Equal(string.Empty, config.Updater.CustomDatabaseRepository);
            Assert.Empty(config.WorkspaceTaskbarTitleOrder);
        }

        [Fact]
        public void Serialize_DefaultConfig_WritesCompleteCurrentSchema()
        {
            var yaml = Config.Serialize(new YamlConfig());

            foreach (var field in new[]
                     {
                         "core:", "listen-address:", "listen-port:", "show-first-run-prompt:",
                         "updater:", "is-github-blocked:",
                         "trainer-is-male:", "database-language:", "custom-database-repository:",
                         "force-use-github-to-update:", "language:", "selected:", "misc:",
                         "save-response-for-debug:", "workspace-taskbar-title-order:"
                     })
                Assert.Contains(field, yaml);

            var restored = Config.Deserialize(yaml, "config.yaml");
            Assert.NotNull(restored.Core.ListenAddress);
            Assert.NotNull(restored.Updater.DatabaseLanguage);
            Assert.Equal(string.Empty, restored.Updater.CustomDatabaseRepository);
            Assert.NotNull(restored.WorkspaceTaskbarTitleOrder);
        }

        [Fact]
        public void CurrentSchema_RoundTrips_PreservingValues()
        {
            var original = new YamlConfig
            {
                Core = new CoreConfig
                {
                    ListenAddress = "0.0.0.0",
                    ListenPort = 5000,
                    ShowFirstRunPrompt = false
                },
                Updater = new UpdaterConfig
                {
                    TrainerIsMale = false,
                    DatabaseLanguage = "zh-CN",
                    CustomDatabaseRepository = "https://example.com/repo",
                    ForceUseGithubToUpdate = true
                },
                Language = new LanguageConfig { Selected = LanguageConfig.Language.Japanese },
                Misc = new MiscConfig { SaveResponseForDebug = true },
                WorkspaceTaskbarTitleOrder = ["插件", "启动信息", "遥测"]
            };

            var yaml = Config.Serialize(original);
            var restored = Config.Deserialize(yaml, "config.yaml");

            Assert.DoesNotContain("request-additional-header", yaml);

            Assert.Equal("0.0.0.0", restored.Core.ListenAddress);
            Assert.Equal(5000, restored.Core.ListenPort);
            Assert.False(restored.Core.ShowFirstRunPrompt);
            Assert.False(restored.Updater.TrainerIsMale);
            Assert.Equal("zh-CN", restored.Updater.DatabaseLanguage);
            Assert.Equal("https://example.com/repo", restored.Updater.CustomDatabaseRepository);
            Assert.True(restored.Updater.ForceUseGithubToUpdate);
            Assert.True(restored.Misc.SaveResponseForDebug);
            Assert.Equal(LanguageConfig.Language.Japanese, restored.Language.Selected);
            Assert.Equal(["插件", "启动信息", "遥测"], restored.WorkspaceTaskbarTitleOrder);
        }

        [Fact]
        public void HyphenatedNamingConvention_EmitsKebabCaseKeys()
        {
            var yaml = Config.Serialize(new YamlConfig());

            Assert.Contains("listen-port", yaml);
            Assert.Contains("listen-address", yaml);
            Assert.Contains("workspace-taskbar-title-order", yaml);
            Assert.DoesNotContain("ListenPort", yaml);
        }

        [Fact]
        public void CoreConfig_Defaults_MatchSource()
        {
            var core = new CoreConfig();
            Assert.Equal("127.0.0.1", core.ListenAddress);
            Assert.Equal(4693, core.ListenPort);
            Assert.True(core.ShowFirstRunPrompt);
        }

        [Fact]
        public void OtherConfig_Defaults_MatchSource()
        {
            Assert.False(new MiscConfig().SaveResponseForDebug);

            var updater = new UpdaterConfig();
            // TrainerIsMale 源码默认 true；DatabaseLanguage 源码默认 "ja-JP"
            Assert.True(updater.TrainerIsMale);
            Assert.Equal("ja-JP", updater.DatabaseLanguage);
            Assert.Equal(string.Empty, updater.CustomDatabaseRepository);

            // LanguageConfig.Selected 源码默认 AutoDetect
            Assert.Equal(LanguageConfig.Language.AutoDetect, new LanguageConfig().Selected);
        }
    }

    /// <summary>
    /// <see cref="LanguageConfig.GetCulture"/> 的 enum→culture 映射逐项测。
    /// 该方法读取全局 <c>Config.Language.Selected</c>，故必须临时写入静态状态 <c>Config.Current</c>——
    /// 用反射设置（避开文件 IO 的 Initialize），跑完恢复原值。归入 "Database" collection 串行，避免与其它会动静态状态的测试并发。
    /// </summary>
    [Collection("Database")]
    public class LanguageConfigGetCultureTests
    {
        // Config.Current 是 private static 属性；用反射读写以临时注入一个带指定语言的 YamlConfig
        static readonly PropertyInfo CurrentProp =
            typeof(Config).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;

        static void SetSelected(LanguageConfig.Language lang)
        {
            var langConfig = new LanguageConfig();
            // Selected 的 setter 是 private，反射调用
            typeof(LanguageConfig).GetProperty(nameof(LanguageConfig.Selected))!
                .GetSetMethod(nonPublic: true)!
                .Invoke(langConfig, [lang]);
            CurrentProp.SetValue(null, new YamlConfig { Language = langConfig });
        }

        [Theory]
        [InlineData(LanguageConfig.Language.SimplifiedChinese, "zh-CN")]
        [InlineData(LanguageConfig.Language.Japanese, "ja-JP")]
        [InlineData(LanguageConfig.Language.English, "en-US")]
        public void GetCulture_MapsEnumToExpectedCulture(LanguageConfig.Language lang, string expected)
        {
            var saved = CurrentProp.GetValue(null);
            try
            {
                SetSelected(lang);
                Assert.Equal(expected, LanguageConfig.GetCulture());
            }
            finally
            {
                CurrentProp.SetValue(null, saved); // 恢复静态状态
            }
        }

        [Fact]
        public void GetCulture_AutoDetect_MapsCurrentThreadCultureToSupportedUiLanguage()
        {
            var saved = CurrentProp.GetValue(null);
            try
            {
                SetSelected(LanguageConfig.Language.AutoDetect);
                // AutoDetect 不再裸用 OS 区域名(那会让繁中等无对应资源的区域回退英文)，
                // 而是经 AutoDetectCulture 归一到已有 UI 资源的语言。这里断言两者一致(与运行环境无关)。
                Assert.Equal(
                    LanguageConfig.AutoDetectCulture(System.Threading.Thread.CurrentThread.CurrentCulture.Name),
                    LanguageConfig.GetCulture());
            }
            finally
            {
                CurrentProp.SetValue(null, saved);
            }
        }

        [Theory]
        // #4 回归:繁中(zh-TW)等无对应 .resx 的区域,旧逻辑裸用 OS 区域名→ResourceManager 找不到→回退 invariant 英文,
        // 导致繁中系统下整个 UI 变英文。修复后所有 zh-* 归到唯一的中文资源 zh-CN。
        [InlineData("zh-TW", "zh-CN")]
        [InlineData("zh-HK", "zh-CN")]
        [InlineData("zh-CN", "zh-CN")]
        [InlineData("zh", "zh-CN")]
        [InlineData("ja-JP", "ja-JP")]
        [InlineData("ja", "ja-JP")]
        [InlineData("en-US", "en-US")]
        [InlineData("en-GB", "en-US")]
        [InlineData("fr-FR", "en-US")]
        [InlineData("ko-KR", "en-US")]
        public void AutoDetectCulture_MapsOsCultureToNearestSupported(string osCulture, string expected)
        {
            Assert.Equal(expected, LanguageConfig.AutoDetectCulture(osCulture));
        }
    }

    /// <summary>
    /// <see cref="Database"/> 里几张硬编码静态表的确定性单测。对表本身只读、不 mutate，
    /// 但「触碰 Database 任意成员」会触发其静态构造器——而 <c>Database.cs</c> 的静态字段初始化器读了
    /// <c>Config.Updater</c>，测试环境下 Config 未初始化会 NRE。故沿用本仓库既有约定：
    /// ctor 里反射注入一个 YamlConfig 到 private static <c>Config.Current</c>（避开会写盘的 <c>Config.Initialize</c>），
    /// 并归入 "Database" collection 串行执行（与其它会动 Config/Database 静态状态的测试互斥）。
    /// </summary>
    [Collection("Database")]
    public class DatabaseStaticTableTests
    {
        public DatabaseStaticTableTests()
        {
            var currentProp = typeof(Config).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (currentProp.GetValue(null) is null)
                currentProp.SetValue(null, new YamlConfig());
        }

        [Fact]
        public void StatusToPoint_HeadValues_MatchSource()
        {
            // 源码开头: [0,1,1,2,2,3,3,4,4,5,5,6,...]
            var t = Database.StatusToPoint;
            Assert.Equal(0, t[0]);
            Assert.Equal(1, t[1]);
            Assert.Equal(1, t[2]);
            Assert.Equal(2, t[3]);
            Assert.Equal(2, t[4]);
            Assert.Equal(3, t[5]);
            Assert.Equal(4, t[7]);
            Assert.Equal(5, t[9]);
        }

        [Fact]
        public void ClimaxItem_KnownKeys_MatchSource()
        {
            var items = Database.ClimaxItem;
            Assert.Equal(Localization.Database.ClimaxItem_1001, items[1001]);
            Assert.Equal(Localization.Database.ClimaxItem_1101, items[1101]);
            Assert.Equal(Localization.Database.ClimaxItem_2001, items[2001]);
            Assert.Equal(Localization.Database.ClimaxItem_10001, items[10001]);
        }

        [Fact]
        public void ClimaxItem_MissingKey_ReturnsUnknownFallback()
        {
            Assert.Equal(Localization.NameManager.I18N_Unknown, Database.ClimaxItem[999999]);
        }

        [Fact]
        public void NullableIntStringDictionary_Indexer_DefaultsToUnknown()
        {
            var dict = new NullableIntStringDictionary { { 42, "answer" } };
            Assert.Equal("answer", dict[42]);   // 命中
            Assert.Equal(Localization.NameManager.I18N_Unknown, dict[0]);
        }
    }

    [Collection("PluginRuntime")]
    public class DatabaseInitializeMissingFilesTests(PluginRuntimeFixture runtime)
    {
        [Fact]
        public async Task Initialize_WithMissingDataFiles_IsUnavailableAndDataAccessFailsClearly()
        {
            const string scenario = "database-missing";
            if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                Assert.Equal(
                    "Unavailable|Unavailable|True",
                    await TerminalUiLifecycleProcessTests.RunChildAsync(
                        scenario,
                        typeof(DatabaseInitializeMissingFilesTests),
                        nameof(Initialize_WithMissingDataFiles_IsUnavailableAndDataAccessFailsClearly)));
                return;
            }

            var directory = Path.Combine(Path.GetTempPath(), $"ura-database-missing-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var previousDirectory = Directory.GetCurrentDirectory();
            var logs = new List<UiLogLine>();
            void ObserveLog(UiLogLine entry) => logs.Add(entry);
            runtime.Host.LogAdded += ObserveLog;
            try
            {
                Directory.SetCurrentDirectory(directory);
                var result = await Database.Initialize();
                await runtime.Host.FlushAsync();
                var warnings = logs.Where(entry => entry.Text.StartsWith("[Database]", StringComparison.Ordinal)
                                                   && entry.Severity == UiSeverity.Warning).ToArray();
                Assert.Equal(8, warnings.Length);
                foreach (var file in new[]
                         {
                             Database.EVENT_NAME_FILEPATH, Database.NAMES_FILEPATH, Database.SKILLS_FILEPATH,
                             Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, Database.TALENT_SKILLS_FILEPATH,
                             Database.FACTOR_IDS_FILEPATH, Database.SADDLE_IDS_FILEPATH, Database.SUCCESSION_RELATION_FILEPATH
                         })
                    Assert.Contains(warnings, entry => entry.Text.Contains(file, StringComparison.Ordinal));
                var message = Assert.Throws<InvalidOperationException>(() => Database.Events).Message;
                TerminalUiLifecycleChildProcess.WriteResult($"{result}|{Database.Availability}|{message == Localization.Database.I18N_NotLoaded}");
            }
            finally
            {
                runtime.Host.LogAdded -= ObserveLog;
                Directory.SetCurrentDirectory(previousDirectory);
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public async Task Initialize_PublishesOnlyACompleteSnapshot()
        {
            const string scenario = "database-atomic-snapshot";
            if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                Assert.Equal(
                    "Ready|Unavailable|Ready|True|event",
                    await TerminalUiLifecycleProcessTests.RunChildAsync(
                        scenario,
                        typeof(DatabaseInitializeMissingFilesTests),
                        nameof(Initialize_PublishesOnlyACompleteSnapshot)));
                return;
            }

            var directory = Path.Combine(Path.GetTempPath(), $"ura-database-ready-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var previousDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(directory);
                await WriteAsync(directory, Database.EVENT_NAME_FILEPATH, new[] { new Story { Id = 7, Name = "event" } });
                await WriteAsync(directory, Database.NAMES_FILEPATH, new List<BaseName> { new(1001, "name", "name") }, new() { TypeNameHandling = TypeNameHandling.All });
                await WriteAsync(directory, Database.SKILLS_FILEPATH, Array.Empty<SkillData>());
                await WriteAsync(directory, Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, Array.Empty<SkillUpgradeSpeciality>());
                await WriteAsync(directory, Database.TALENT_SKILLS_FILEPATH, new Dictionary<int, TalentSkillData[]>());
                await WriteAsync(directory, Database.FACTOR_IDS_FILEPATH, new Dictionary<int, string>());
                await WriteAsync(directory, Database.SADDLE_IDS_FILEPATH, Array.Empty<int>());
                await WriteAsync(directory, Database.SUCCESSION_RELATION_FILEPATH, new SuccessionRelationTable());

                var ready = await Database.Initialize();
                if (ready != DatabaseAvailability.Ready)
                {
                    await runtime.Host.FlushAsync();
                    Assert.Fail(await runtime.Terminal.CaptureScreenAsync());
                }
                var publishedEvents = Database.Events;

                File.Delete(Path.Combine(directory, Database.NAMES_FILEPATH));
                var unavailable = await Database.Initialize();
                await runtime.Host.FlushAsync();
                TerminalUiLifecycleChildProcess.WriteResult(
                    $"{ready}|{unavailable}|{Database.Availability}|{ReferenceEquals(publishedEvents, Database.Events)}|{Database.Events[7].Name}");
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public async Task Initialize_WithUnknownSkillUpgradeConditions_WarnsOnceAndRemainsReady()
        {
            const string scenario = "database-unknown-skill-upgrade-condition";
            if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                Assert.Equal(
                    "Ready|1|Warning|True",
                    await TerminalUiLifecycleProcessTests.RunChildAsync(
                        scenario,
                        typeof(DatabaseInitializeMissingFilesTests),
                        nameof(Initialize_WithUnknownSkillUpgradeConditions_WarnsOnceAndRemainsReady)));
                return;
            }

            const int conditionId = 11320104;
            var unknownCondition = new TalentSkillData.UpgradeCondition
            {
                ConditionId = conditionId,
                Type = (TalentSkillData.UpgradeCondition.ConditionType)999
            };
            var upgradeSkills = new Dictionary<int, TalentSkillData.UpgradeCondition[]>
            {
                [200] = [unknownCondition],
                [201] = [new() { ConditionId = 41201101, Type = TalentSkillData.UpgradeCondition.ConditionType.None }]
            };

            var directory = Path.Combine(Path.GetTempPath(), $"ura-database-unknown-condition-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var previousDirectory = Directory.GetCurrentDirectory();
            List<UiLogLine> logs = [];
            void ObserveLog(UiLogLine line) => logs.Add(line);
            runtime.Host.LogAdded += ObserveLog;
            try
            {
                Directory.SetCurrentDirectory(directory);
                await WriteAsync(directory, Database.EVENT_NAME_FILEPATH, new[] { new Story { Id = 7, Name = "event" } });
                await WriteAsync(directory, Database.NAMES_FILEPATH, new List<BaseName> { new(1001, "name", "name") }, new() { TypeNameHandling = TypeNameHandling.All });
                await WriteAsync(directory, Database.SKILLS_FILEPATH, Array.Empty<SkillData>());
                await WriteAsync(directory, Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, new[]
                {
                    new SkillUpgradeSpeciality
                    {
                        ScenarioId = 1,
                        BaseSkillId = 100,
                        UpgradeSkills = upgradeSkills
                    }
                });
                await WriteAsync(directory, Database.TALENT_SKILLS_FILEPATH, new Dictionary<int, TalentSkillData[]>
                {
                    [1] =
                    [
                        new()
                        {
                            SkillId = 100,
                            Rank = 3,
                            UpgradeSkills = upgradeSkills
                        }
                    ]
                });
                await WriteAsync(directory, Database.FACTOR_IDS_FILEPATH, new Dictionary<int, string>());
                await WriteAsync(directory, Database.SADDLE_IDS_FILEPATH, Array.Empty<int>());
                await WriteAsync(directory, Database.SUCCESSION_RELATION_FILEPATH, new SuccessionRelationTable());

                var ready = await Database.Initialize();
                await runtime.Host.FlushAsync();
                await runtime.Terminal.WaitForScreenAsync($"conditionId={conditionId}");
                var warnings = logs
                    .Where(x => x.Text.Contains("conditionId=", StringComparison.Ordinal))
                    .ToArray();
                var screen = await runtime.Terminal.CaptureScreenAsync();
                var severity = warnings.Length == 1 ? warnings[0].Severity.ToString() : "n/a";
                TerminalUiLifecycleChildProcess.WriteResult(
                    $"{ready}|{warnings.Length}|{severity}|" +
                    $"{screen.Contains($"conditionId={conditionId}", StringComparison.Ordinal)}");
            }
            finally
            {
                runtime.Host.LogAdded -= ObserveLog;
                Directory.SetCurrentDirectory(previousDirectory);
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public async Task Initialize_EvolutionCandidates_RespectServerProgressAndMasterGroups()
        {
            const string scenario = "database-skill-evolution-groups";
            if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                Assert.Equal(
                    "Ready|12|0",
                    await TerminalUiLifecycleProcessTests.RunChildAsync(
                        scenario,
                        typeof(DatabaseInitializeMissingFilesTests),
                        nameof(Initialize_EvolutionCandidates_RespectServerProgressAndMasterGroups)));
                return;
            }

            var directory = Path.Combine(Path.GetTempPath(), $"ura-evolution-groups-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var previousDirectory = Directory.GetCurrentDirectory();
            List<UiLogLine> logs = [];
            void ObserveLog(UiLogLine line) => logs.Add(line);
            runtime.Host.LogAdded += ObserveLog;
            try
            {
                Directory.SetCurrentDirectory(directory);
                await WriteAsync(directory, Database.EVENT_NAME_FILEPATH, Array.Empty<Story>());
                await WriteAsync(directory, Database.NAMES_FILEPATH, new List<BaseName>(), new() { TypeNameHandling = TypeNameHandling.All });
                var skillIds = new[] { 201662, 203361, 200511, 412011, 412051, 100101211 };
                await WriteAsync(directory, Database.SKILLS_FILEPATH, skillIds.Select(id => new SkillData
                {
                    Id = id, GroupId = id, Name = $"skill{id}", Rarity = 2, Rate = 2, Cost = 180, Grade = 250, Propers = []
                }).ToArray());
                // 主表：412051 的两个跑法条件属于 num=1，汤浴会属于 num=2。
                await WriteAsync(directory, Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, new[]
                {
                    new SkillUpgradeSpeciality
                    {
                        ScenarioId = 12, BaseSkillId = 201662, SkillId = 412011,
                        UpgradeSkills = { [412011] = [new() { ConditionId = 41201101, Group = 1 }] }
                    },
                    new SkillUpgradeSpeciality
                    {
                        ScenarioId = 12, BaseSkillId = 203361, SkillId = 412051,
                        UpgradeSkills =
                        {
                            [412051] =
                            [
                                new() { ConditionId = 41205101, Group = 1, Type = TalentSkillData.UpgradeCondition.ConditionType.Proper, Requirement = 3, AdditionalRequirement = 2 },
                                new() { ConditionId = 41205102, Group = 1, Type = TalentSkillData.UpgradeCondition.ConditionType.Proper, Requirement = 4, AdditionalRequirement = 2 },
                                new() { ConditionId = 41205103, Group = 2 }
                            ]
                        }
                    }
                });
                // 主表：日本杯/GⅠ胜场属于 num=1，智力属于 num=2。
                await WriteAsync(directory, Database.TALENT_SKILLS_FILEPATH, new Dictionary<int, TalentSkillData[]>
                {
                    [100101] =
                    [
                        new()
                        {
                            SkillId = 200511, Rank = 5,
                            UpgradeSkills =
                            {
                                [100101211] =
                                [
                                    new() { ConditionId = 10010103, Group = 1 },
                                    new() { ConditionId = 10010104, Group = 1 },
                                    new() { ConditionId = 10010105, Group = 2 }
                                ]
                            }
                        }
                    ]
                });
                await WriteAsync(directory, Database.FACTOR_IDS_FILEPATH, new Dictionary<int, string>());
                await WriteAsync(directory, Database.SADDLE_IDS_FILEPATH, Array.Empty<int>());
                await WriteAsync(directory, Database.SUCCESSION_RELATION_FILEPATH, new SuccessionRelationTable());
                Assert.Equal(DatabaseAvailability.Ready, await Database.Initialize());

                var chara = new Gallop.SingleModeChara
                {
                    card_id = 100101, talent_level = 5, scenario_id = 12,
                    chara_effect_id_array = [], skill_array = [],
                    skill_tips_array = [new() { group_id = 201662, rarity = 2 }, new() { group_id = 203361, rarity = 2 }],
                    skill_upgrade_info_array =
                    [
                        new() { condition_id = 41201101, total_count = 3 },
                        new() { condition_id = 41205101, total_count = 2 },
                        new() { condition_id = 41205102, total_count = 2 },
                        new() { condition_id = 41205103, total_count = 1 },
                        new() { condition_id = 10010103, total_count = 1 },
                        new() { condition_id = 10010104, total_count = 2 },
                        new() { condition_id = 10010105, total_count = 800 }
                    ]
                };
                var manager = Database.Skills.Apply(chara);
                var cases = new (int[] Counts, SkillProper.StyleType Style, int[] Expected)[]
                {
                    ([0, 0, 0, 0, 0, 0, 0], default, []),
                    ([3, 0, 0, 0, 0, 0, 0], default, [412011]),
                    ([0, 2, 0, 0, 0, 0, 0], default, []),
                    ([0, 0, 0, 1, 0, 0, 0], default, []),
                    ([0, 2, 0, 1, 0, 0, 0], default, [412051]),
                    ([0, 0, 2, 1, 0, 0, 0], default, [412051]),
                    ([0, 0, 0, 1, 0, 0, 0], SkillProper.StyleType.Sashi, [412051]),
                    ([0, 0, 0, 0, 0, 0, 0], SkillProper.StyleType.Oikomi, []),
                    ([0, 0, 0, 0, 1, 0, 0], default, []),
                    ([0, 0, 0, 0, 0, 0, 800], default, []),
                    ([0, 0, 0, 0, 1, 0, 800], default, [100101211]),
                    ([0, 0, 0, 0, 0, 2, 800], default, [100101211])
                };
                foreach (var (counts, style, expected) in cases)
                {
                    for (var i = 0; i < counts.Length; i++)
                        chara.skill_upgrade_info_array[i].current_count = counts[i];
                    var willLearn = style == default ? Array.Empty<SkillData>() : Enumerable.Range(1, 2)
                        .Select(id => new SkillData { Id = id, Name = $"skill{id}", Propers = [new() { Style = style }] }).ToArray();
                    manager.Evolve(chara, willLearn);
                    Assert.Equal(expected, manager.SelectMany(x => x.Upgrades).Select(x => x.Id).Order().ToArray());
                }
                await runtime.Host.FlushAsync();
                var warnings = logs.Count(x => x.Text.Contains("conditionId=", StringComparison.Ordinal));
                TerminalUiLifecycleChildProcess.WriteResult($"Ready|{cases.Length}|{warnings}");
            }
            finally
            {
                runtime.Host.LogAdded -= ObserveLog;
                Directory.SetCurrentDirectory(previousDirectory);
                Directory.Delete(directory, true);
            }
        }

        private static async Task WriteAsync<T>(
            string directory,
            string fileName,
            T value,
            JsonSerializerSettings? settings = null)
        {
            await using var file = File.Create(Path.Combine(directory, fileName));
            await using var brotli = new BrotliStream(file, CompressionMode.Compress);
            await using var writer = new StreamWriter(brotli, Encoding.UTF8);
            await writer.WriteAsync(JsonConvert.SerializeObject(value, settings));
        }
    }

    /// <summary>
    /// <see cref="UraCoreHelper.ExtractGamePathPrefix"/> 纯函数测：含 umamusume.exe（大小写不敏感）取其前缀，不含则 null。
    /// </summary>
    public class UraCoreHelperTests
    {
        [Fact]
        public void ExtractGamePathPrefix_ReturnsPrefix_WhenContainsExe()
        {
            Assert.Equal("D:/Games/", UraCoreHelper.ExtractGamePathPrefix("D:/Games/umamusume.exe"));
        }

        [Theory]
        [InlineData("D:/Games/UMAMUSUME.EXE", "D:/Games/")]
        [InlineData("C:/x/Umamusume.Exe", "C:/x/")]
        public void ExtractGamePathPrefix_IsCaseInsensitive(string input, string expected)
        {
            // IndexOf 用 OrdinalIgnoreCase，故大小写变体也能匹配并取到前缀
            Assert.Equal(expected, UraCoreHelper.ExtractGamePathPrefix(input));
        }

        [Fact]
        public void ExtractGamePathPrefix_ExeAtRoot_ReturnsEmptyPrefix()
        {
            // exe 名出现在最前 → idx==0 → 前缀为空串（注意：不是 null）
            Assert.Equal(string.Empty, UraCoreHelper.ExtractGamePathPrefix("umamusume.exe"));
        }

        [Fact]
        public void ExtractGamePathPrefix_ReturnsNull_WhenNoExe()
        {
            Assert.Null(UraCoreHelper.ExtractGamePathPrefix("D:/Games/other.exe"));
        }

        [Fact]
        public void ExtractGamePathPrefix_ReturnsNull_WhenEmpty()
        {
            Assert.Null(UraCoreHelper.ExtractGamePathPrefix(string.Empty));
        }
    }
}
