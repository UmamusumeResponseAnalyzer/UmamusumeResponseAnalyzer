using System.Reflection;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// Entities/ 里的纯逻辑：UmaName.CharaId 切片、SupportCardName.TypeName 映射、
    /// 以及 Motivation 的隐式转换 / 着色。
    /// 这些都不触碰 <see cref="Database.Names"/>，故无需 [Collection("Database")]。
    /// </summary>
    public class EntitiesTests
    {
        // ---------- UmaName.CharaId 切片解析 ----------
        // 规则(Name.cs)：charaId==0(默认)时由 id.ToString() 推算：
        //   首字符=='9' → id.ToString()[1..5]（跳过开头的9，取随后4位）
        //   否则        → id.ToString()[..4]（取前4位）
        // charaId 显式给非0值时直接采用、不做切片。

        [Fact]
        public void UmaName_CharaId_FourDigitNon9_TakesAsIs()
        {
            // "1004"[..4] => 1004
            var uma = new UmaName(1004, "测试");
            Assert.Equal(1004, uma.CharaId);
        }

        [Fact]
        public void UmaName_CharaId_FiveDigitNon9_TakesFirstFour()
        {
            // "10046"[..4] => "1004" => 1004（末位被切掉）
            var uma = new UmaName(10046, "测试");
            Assert.Equal(1004, uma.CharaId);
        }

        [Fact]
        public void UmaName_CharaId_NinePrefixed_SkipsLeading9_TakesNextFour()
        {
            // 首字符'9' 走 [1..5] 分支："90004"[1..5] => "0004" => 4
            var uma = new UmaName(90004, "测试");
            Assert.Equal(4, uma.CharaId);
        }

        [Fact]
        public void UmaName_CharaId_NinePrefixed_PreservesInnerDigits()
        {
            // "91234"[1..5] => "1234" => 1234
            var uma = new UmaName(91234, "测试");
            Assert.Equal(1234, uma.CharaId);
        }

        [Fact]
        public void UmaName_CharaId_ExplicitNonZero_BypassesSlicing()
        {
            // charaId 非0 → 直接用 2002，忽略 id(99999) 的切片
            var uma = new UmaName(99999, "测试", 2002);
            Assert.Equal(2002, uma.CharaId);
        }

        // ---------- SupportCardName.TypeName 映射 ----------
        // 101=>[速] 102=>[力] 103=>[根] 105=>[耐] 106=>[智] 0=>[友] 其它=>""（注意 104 未定义）
        [Theory]
        [InlineData(101, "[速]")]
        [InlineData(102, "[力]")]
        [InlineData(103, "[根]")]
        [InlineData(105, "[耐]")]
        [InlineData(106, "[智]")]
        [InlineData(0, "[友]")]
        [InlineData(104, "")]   // 未在 switch 中列出 → default 空串
        [InlineData(999, "")]
        public void SupportCardName_TypeName_MapsByType(int type, string expected)
        {
            var card = new SupportCardName(10001, "卡名", type, 1004);
            Assert.Equal(expected, card.TypeName);
        }

        // ---------- Motivation 隐式转换 ----------
        // implicit int => motivation 数值；implicit string => enumString
        [Theory]
        [InlineData(1, "绝不调")]
        [InlineData(2, "不调")]
        [InlineData(3, "普通")]
        [InlineData(4, "好调")]
        [InlineData(5, "绝好调")]
        public void Motivation_ImplicitConversions(int value, string expectedString)
        {
            var m = new Motivation(value);
            int asInt = m;
            string asString = m;
            Assert.Equal(value, asInt);
            Assert.Equal(expectedString, asString);
        }

        [Fact]
        public void Motivation_StaticFactories_HaveExpectedValues()
        {
            Assert.Equal(5, (int)Motivation.Best);
            Assert.Equal(4, (int)Motivation.Good);
            Assert.Equal(3, (int)Motivation.Normal);
            Assert.Equal(2, (int)Motivation.Bad);
            Assert.Equal(1, (int)Motivation.Worst);
        }

        // ToColoredString 颜色：5=>green 4=>yellow 3/2/1=>red；主体是 enumString 标签。
        // 源码 $"[color]{this}[/]" 经 ToString() 重写后渲染为中文标签(绝好调/好调/普通/不调/绝不调)。
        [Theory]
        [InlineData(5, "green", "绝好调")]
        [InlineData(4, "yellow", "好调")]
        [InlineData(3, "red", "普通")]
        [InlineData(2, "red", "不调")]
        [InlineData(1, "red", "绝不调")]
        public void Motivation_ToColoredString(int value, string color, string label)
        {
            var expected = $"[{color}]{label}[/]";
            Assert.Equal(expected, new Motivation(value).ToColoredString());
        }

    }

    /// <summary>
    /// 需要查 <see cref="Database.Names"/> 的 Entities 逻辑（CharacterName / FullName / SimpleName）。
    /// 归入 "Database" collection 串行执行，避免与其它 seed 全局静态状态的测试竞争。
    /// </summary>
    [Collection("Database")]
    public class EntitiesDatabaseTests
    {
        public EntitiesDatabaseTests()
        {
            // 触碰 Database 任意成员都会跑其静态构造器，而那里(Database.cs)用到 Config.Updater，
            // 测试环境下 Config 未初始化会 NRE。沿用 ConfigDatabaseTests 的约定：反射注入一个
            // YamlConfig 到 private static Config.Current（避开会写 config.yaml 的 Config.Initialize），
            // 让 Database 的静态初始化拿得到非 null 的 Updater。
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

            // 用普通 BaseName 作为角色条目：NameManager[id] 对 BaseName 走 _ => value.Name。
            // CharaId=1004 -> 本名"美浦波旁"；1006 -> "无声铃鹿"。
            Database.Names = new NameManager(
            [
                new BaseName(1004, "美浦波旁"),
                new BaseName(1006, "无声铃鹿"),
            ]);
        }

        [Fact]
        public void UmaName_CharacterName_LooksUpByCharaId()
        {
            // id=1004 → CharaId 切片得 1004 → 查表 "美浦波旁"
            var uma = new UmaName(1004, "[CODE：グラサージュ]");
            Assert.Equal("美浦波旁", uma.CharacterName);
        }

        [Fact]
        public void UmaName_FullName_ConcatenatesNameAndCharacterName()
        {
            // FullName = Name + CharacterName
            var uma = new UmaName(1004, "[CODE：グラサージュ]");
            Assert.Equal("[CODE：グラサージュ]美浦波旁", uma.FullName);
        }

        [Fact]
        public void UmaName_CharacterName_UnknownCharaId_ReturnsUnknownPlaceholder()
        {
            // 表中无 1999 → NameManager 返回 I18N_Unknown；这里只断言非空、与已知名不同
            var uma = new UmaName(1999, "某卡");
            Assert.False(string.IsNullOrEmpty(uma.CharacterName));
            Assert.NotEqual("美浦波旁", uma.CharacterName);
        }

        [Fact]
        public void SupportCardName_CharacterName_LooksUpByCharaId()
        {
            // CharaId=1006 → "无声铃鹿"
            var card = new SupportCardName(20001, "卡名", 106, 1006);
            Assert.Equal("无声铃鹿", card.CharacterName);
        }

        [Fact]
        public void SupportCardName_FullName_IsCardNamePlusCharacterName()
        {
            // FullName = Name + CharacterName
            var card = new SupportCardName(20001, "[ミッション]", 106, 1004);
            Assert.Equal("[ミッション]美浦波旁", card.FullName);
        }

        [Fact]
        public void SupportCardName_SimpleName_IsTypeNamePlusCharacterName()
        {
            // SimpleName = TypeName + CharacterName，例如 [智]美浦波旁
            var card = new SupportCardName(20001, "[ミッション]", 106, 1004);
            Assert.Equal("[智]美浦波旁", card.SimpleName);
        }
    }
}
