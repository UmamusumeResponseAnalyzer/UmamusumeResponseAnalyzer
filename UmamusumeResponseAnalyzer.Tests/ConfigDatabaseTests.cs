using System.Reflection;
using UmamusumeResponseAnalyzer;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// Config 的 YAML 往返 + 各 sub-config 默认值的确定性单测。
    /// 不触碰 <see cref="Config.Initialize"/>/<see cref="Config.Save"/>（那是文件 IO）——
    /// 这里自建一套与 <c>Config.cs</c> 顶部 <c>_serializer/_deserializer</c> 等价的
    /// SerializerBuilder/DeserializerBuilder（同样的 HyphenatedNamingConvention），只验证序列化逻辑本身。
    /// </summary>
    public class ConfigSerializationTests
    {
        // 复刻 Config.cs 第 19/20 行的两个 builder 设置，保持命名约定一致
        static readonly ISerializer Serializer = new SerializerBuilder()
            .WithQuotingNecessaryStrings()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .Build();
        static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .Build();

        [Fact]
        public void YamlConfig_RoundTrips_PreservesKeyFields()
        {
            var original = new YamlConfig
            {
                Core = new CoreConfig
                {
                    ListenAddress = "0.0.0.0",
                    ListenPort = 5000,
                    RequestAdditionalHeader = true,
                    ShowFirstRunPrompt = false
                },
                Repository = new RepositoryConfig { Targets = ["a", "b", "c"] },
                Plugin = new PluginConfig(),
                Updater = new UpdaterConfig
                {
                    TrainerIsMale = false,
                    DatabaseLanguage = "zh-CN",
                    CustomDatabaseRepository = "https://example.com/repo",
                    ForceUseGithubToUpdate = true
                },
                Language = new LanguageConfig(),
                Misc = new MiscConfig { SaveResponseForDebug = true }
            };

            var yaml = Serializer.Serialize(original);
            var restored = Deserializer.Deserialize<YamlConfig>(yaml);

            // Core
            Assert.Equal("0.0.0.0", restored.Core.ListenAddress);
            Assert.Equal(5000, restored.Core.ListenPort);
            Assert.True(restored.Core.RequestAdditionalHeader);
            Assert.False(restored.Core.ShowFirstRunPrompt);
            // Repository（List<string> 往返保序）
            Assert.Equal(["a", "b", "c"], restored.Repository.Targets);
            // Updater
            Assert.False(restored.Updater.TrainerIsMale);
            Assert.Equal("zh-CN", restored.Updater.DatabaseLanguage);
            Assert.Equal("https://example.com/repo", restored.Updater.CustomDatabaseRepository);
            Assert.True(restored.Updater.ForceUseGithubToUpdate);
            // Misc
            Assert.True(restored.Misc.SaveResponseForDebug);
        }

        [Fact]
        public void HyphenatedNamingConvention_EmitsKebabCaseKeys()
        {
            // ListenPort / RequestAdditionalHeader 等多词属性应被序列化成 listen-port / request-additional-header
            var yaml = Serializer.Serialize(new YamlConfig { Core = new CoreConfig() });

            Assert.Contains("listen-port", yaml);
            Assert.Contains("listen-address", yaml);
            Assert.Contains("request-additional-header", yaml);
            // 不应出现原始 PascalCase
            Assert.DoesNotContain("ListenPort", yaml);
        }

        [Fact]
        public void CoreConfig_Defaults_MatchSource()
        {
            var core = new CoreConfig();
            Assert.Equal("127.0.0.1", core.ListenAddress);
            Assert.Equal(4693, core.ListenPort);
            Assert.False(core.RequestAdditionalHeader);
            Assert.True(core.ShowFirstRunPrompt);
        }

        [Fact]
        public void OtherConfig_Defaults_MatchSource()
        {
            Assert.Empty(new RepositoryConfig().Targets);
            Assert.False(new MiscConfig().SaveResponseForDebug);

            var updater = new UpdaterConfig();
            // TrainerIsMale 源码默认 true；DatabaseLanguage 源码默认 "ja-JP"
            Assert.True(updater.TrainerIsMale);
            Assert.Equal("ja-JP", updater.DatabaseLanguage);

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
                currentProp.SetValue(null, new YamlConfig
                {
                    Core = new(),
                    Repository = new(),
                    Plugin = new(),
                    Updater = new(),
                    Language = new(),
                    Misc = new()
                });
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
            Assert.Equal("速+3", items[1001]);
            Assert.Equal("速+7", items[1101]);
            Assert.Equal("体力+20", items[2001]);
            Assert.Equal("御守", items[10001]);
        }

        [Fact]
        public void ClimaxItem_MissingKey_ReturnsUnknownFallback()
        {
            // ClimaxItem 是 NullableIntStringDictionary，缺省 key 经其 indexer 返回 "未知"
            Assert.Equal("未知", Database.ClimaxItem[999999]);
        }

        [Fact]
        public void NullableIntStringDictionary_Indexer_DefaultsToUnknown()
        {
            // 直接验证该类型 indexer 的缺省返回值（Database.cs 中定义为 "未知"）
            var dict = new NullableIntStringDictionary { { 42, "answer" } };
            Assert.Equal("answer", dict[42]);   // 命中
            Assert.Equal("未知", dict[0]);       // 未命中 → fallback
        }
    }

    /// <summary>
    /// #1 回归:数据文件缺失时 <see cref="Database.Initialize"/> 必须优雅降级——不再对 null 调 .ToDictionary 而崩溃。
    /// 全新用户(还没下数据)选"启动"曾在此抛 ArgumentNullException 拖垮整个程序;修复后应正常完成、各属性保持安全空默认。
    /// 测试在当前 CWD(测试输出目录,无任何 .br)直接调真实 Initialize 走"文件缺失"路径——只 File.Exists 判断、不读真实数据、无文件写入。
    /// 归入 "Database" collection 串行(会动 Database 静态状态)。
    /// </summary>
    [Collection("Database")]
    public class DatabaseInitializeMissingFilesTests
    {
        public DatabaseInitializeMissingFilesTests()
        {
            // 同 DatabaseStaticTableTests:注入 Config.Current,使 Database 静态字段初始化器读 Config.Updater 不 NRE
            var currentProp = typeof(Config).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (currentProp.GetValue(null) is null)
                currentProp.SetValue(null, new YamlConfig
                {
                    Core = new(),
                    Repository = new(),
                    Plugin = new(),
                    Updater = new(),
                    Language = new(),
                    Misc = new()
                });
        }

        [Fact]
        public async Task Initialize_WithMissingDataFiles_DoesNotThrowAndKeepsSafeDefaults()
        {
            // 不抛即过:修复前这里会因 events_*.br 缺失→eventsTask.Result 为 null→null.ToDictionary 抛 ArgumentNullException
            await Database.Initialize();

            Assert.True(Database.Initialized);
            // 缺文件时各属性保持非空安全默认,后续消费(如分析包/技能进化)不会 NPE
            Assert.NotNull(Database.Events);
            Assert.NotNull(Database.SkillUpgradeSpeciality);
            Assert.NotNull(Database.FactorIds);
            Assert.NotNull(Database.SuccessionRelation);
            Assert.NotNull(Database.SuccessionRelation.PointDictionary);
            Assert.NotNull(Database.SuccessionRelation.MemberDictionary);
            Assert.NotNull(SkillManagerGenerator.Default);
            // 空 SkillManager 索引返回 null 而非 NPE
            Assert.Null(SkillManagerGenerator.Default[999999]);
        }

        [Fact]
        public void SuccessionRelationTable_Defaults_AreEmptyNotNull()
        {
            // #1 的一部分:该实体两个字典给了空默认,使 Database.SuccessionRelation = new() 完全安全(内部字典不为 null)
            var table = new Entities.SuccessionRelationTable();
            Assert.NotNull(table.PointDictionary);
            Assert.Empty(table.PointDictionary);
            Assert.NotNull(table.MemberDictionary);
            Assert.Empty(table.MemberDictionary);
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
