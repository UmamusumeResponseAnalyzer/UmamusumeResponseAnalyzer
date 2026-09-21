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
            var uma = new UmaName(1004, "测试", "测试");
            Assert.Equal(1004, uma.CharaId);
        }

        [Fact]
        public void UmaName_CharaId_FiveDigitNon9_TakesFirstFour()
        {
            // "10046"[..4] => "1004" => 1004（末位被切掉）
            var uma = new UmaName(10046, "测试", "测试");
            Assert.Equal(1004, uma.CharaId);
        }

        [Fact]
        public void UmaName_CharaId_NinePrefixed_SkipsLeading9_TakesNextFour()
        {
            // 首字符'9' 走 [1..5] 分支："90004"[1..5] => "0004" => 4
            var uma = new UmaName(90004, "测试", "测试");
            Assert.Equal(4, uma.CharaId);
        }

        [Fact]
        public void UmaName_CharaId_NinePrefixed_PreservesInnerDigits()
        {
            // "91234"[1..5] => "1234" => 1234
            var uma = new UmaName(91234, "测试", "测试");
            Assert.Equal(1234, uma.CharaId);
        }

        [Fact]
        public void UmaName_CharaId_ExplicitNonZero_BypassesSlicing()
        {
            // charaId 非0 → 直接用 2002，忽略 id(99999) 的切片
            var uma = new UmaName(99999, "测试", "测试", 2002);
            Assert.Equal(2002, uma.CharaId);
        }

        // ---------- SupportCardName.TypeName 映射 ----------
        // 101=>[速] 102=>[力] 103=>[根] 105=>[耐] 106=>[智] 0=>[友]，其它显式显示类型 ID。
        [Theory]
        [InlineData(101, "I18N_SpeedSimple")]
        [InlineData(102, "I18N_PowerSimple")]
        [InlineData(103, "I18N_NutsSimple")]
        [InlineData(105, "I18N_StaminaSimple")]
        [InlineData(106, "I18N_WizSimple")]
        [InlineData(0, "I18N_FriendSimple")]
        [InlineData(104, "[104]")]
        [InlineData(999, "[999]")]
        public void SupportCardName_TypeName_MapsByType(int type, string expected)
        {
            var card = new SupportCardName(10001, "卡名", "简称", type, 1004);
            Assert.Equal(expected.StartsWith("I18N_", StringComparison.Ordinal)
                ? $"[{Localization.Game.ResourceManager.GetString(expected, Localization.Game.Culture)}]"
                : expected, card.TypeName);
        }

        [Theory]
        [InlineData(101, 101, true)]
        [InlineData(101, 102, false)]
        [InlineData(0, 102, false)]
        public void SupportCardName_CanTriggerFriendshipTraining_UsesTrainingType(
            int cardType,
            int trainingType,
            bool expected)
        {
            var card = new SupportCardName(30001, "卡名", "简称", cardType, 1004);

            Assert.Equal(expected, card.CanTriggerFriendshipTraining(trainingType));
        }

        [Theory]
        [InlineData(101)]
        [InlineData(105)]
        [InlineData(102)]
        [InlineData(103)]
        [InlineData(106)]
        public void SupportCardName_CanTriggerFriendshipTraining_TreatsLegendGroupCardAsEligible(int trainingType)
        {
            var card = new SupportCardName(30241, "团体卡", "团体", 0, 9047);

            Assert.True(card.CanTriggerFriendshipTraining(trainingType));
        }

        // ---------- Motivation 隐式转换 ----------
        // implicit int => motivation 数值；implicit string => enumString
        [Theory]
        [InlineData(1, "I18N_MotivationWorst")]
        [InlineData(2, "I18N_MotivationBad")]
        [InlineData(3, "I18N_MotivationNormal")]
        [InlineData(4, "I18N_MotivationGood")]
        [InlineData(5, "I18N_MotivationBest")]
        public void Motivation_ImplicitConversions(int value, string expectedString)
        {
            var m = new Motivation(value);
            int asInt = m;
            string asString = m;
            Assert.Equal(value, asInt);
            Assert.Equal(Localization.Game.ResourceManager.GetString(expectedString, Localization.Game.Culture), asString);
            Assert.Equal(asString, m.ToString());
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

    }

    public class NameManagerTests
    {
        [Fact]
        public void DisplayApis_IncludeUnknownIdAndDoNotMutateSourceNames()
        {
            var source = new SupportCardName(30001, "卡名", "波旁", 101, 1001);
            var names = new NameManager([source, new BaseName(1001, "美浦波旁", "波旁")]);

            Assert.Equal($"[{Localization.Game.I18N_SpeedSimple}]美浦波旁", names.DisplayName(30001));
            Assert.Equal($"[{Localization.Game.I18N_SpeedSimple}]波旁", names.DisplayNickname(30001));
            Assert.Equal("波旁", source.Nickname);
            Assert.NotSame(source, names.GetRequiredSupportCard(30001));
            Assert.Contains("9999", names.DisplayName(9999), StringComparison.Ordinal);
            Assert.Contains("9999", names.DisplayNickname(9999), StringComparison.Ordinal);
        }

        [Fact]
        public void TypedApis_DistinguishTryFromRequired()
        {
            var names = new NameManager(
            [
                new BaseName(1001, "美浦波旁", "波旁"),
                new SupportCardName(30001, "卡名", "波旁", 0, 1001),
                new UmaName(100101, "育成卡", "波旁", 1001)
            ]);

            Assert.True(names.TryGetCharacter(1001, out var character));
            Assert.Equal("美浦波旁", character.Name);
            Assert.Same(character, names.GetRequiredCharacter(1001));
            Assert.True(names.GetRequiredSupportCard(30001).IsFriendCard);
            Assert.True(names.TryGetUmamusume(100101, out var umamusume));
            Assert.Same(umamusume, names.GetRequiredUmamusume(100101));
            Assert.False(names.TryGetSupportCard(1001, out _));
            Assert.Throws<KeyNotFoundException>(() => names.GetRequiredSupportCard(1001));
            Assert.Throws<KeyNotFoundException>(() => names.GetRequiredUmamusume(1001));
        }

        [Fact]
        public void RequiredRSupportCardType_FailsForMissingOrDuplicateData()
        {
            var single = new NameManager(
            [
                new BaseName(1001, "美浦波旁", "波旁"),
                new SupportCardName(10001, "R卡", "波旁", 101, 1001)
            ]);
            Assert.Equal(101, single.GetRequiredRSupportCardTypeByCharaId(1001));
            Assert.Throws<KeyNotFoundException>(() => single.GetRequiredRSupportCardTypeByCharaId(9999));

            var duplicate = new NameManager(
            [
                new SupportCardName(10001, "R卡1", "波旁", 101, 1001),
                new SupportCardName(10002, "R卡2", "波旁", 106, 1001)
            ]);
            Assert.Throws<InvalidDataException>(() => duplicate.GetRequiredRSupportCardTypeByCharaId(1001));
        }
    }

    /// <summary>
    /// 需要查 <see cref="Database.Names"/> 的 Entities 逻辑（CharacterName / FullName / SimpleName）。
    /// 归入 "Database" collection 串行执行，避免与其它 seed 全局静态状态的测试竞争。
    /// </summary>
    [Collection("Database")]
    public class EntitiesDatabaseTests
    {
        [Fact]
        public void UmaName_CharacterName_LooksUpByCharaId()
        {
            // id=1004 → CharaId 切片得 1004 → 查表 "美浦波旁"
            var uma = new UmaName(1004, "[CODE：グラサージュ]", "波旁");
            Assert.Equal("美浦波旁", uma.CharacterName);
        }

        [Fact]
        public void UmaName_FullName_ConcatenatesNameAndCharacterName()
        {
            // FullName = Name + CharacterName
            var uma = new UmaName(1004, "[CODE：グラサージュ]", "波旁");
            Assert.Equal("[CODE：グラサージュ]美浦波旁", uma.FullName);
        }

        [Fact]
        public void UmaName_CharacterName_UnknownCharaId_ReturnsUnknownPlaceholder()
        {
            // 表中无 1999 → NameManager 返回 I18N_Unknown；这里只断言非空、与已知名不同
            var uma = new UmaName(1999, "某卡", "某卡");
            Assert.Contains("1999", uma.CharacterName, StringComparison.Ordinal);
        }

        [Fact]
        public void SupportCardName_CharacterName_LooksUpByCharaId()
        {
            // CharaId=1006 → "无声铃鹿"
            var card = new SupportCardName(20001, "卡名", "铃鹿", 106, 1006);
            Assert.Equal("无声铃鹿", card.CharacterName);
        }

        [Fact]
        public void SupportCardName_FullName_IsCardNamePlusCharacterName()
        {
            // FullName = Name + CharacterName
            var card = new SupportCardName(20001, "[ミッション]", "波旁", 106, 1004);
            Assert.Equal("[ミッション]美浦波旁", card.FullName);
        }

        [Fact]
        public void SupportCardName_SimpleName_IsTypeNamePlusCharacterName()
        {
            // SimpleName = TypeName + CharacterName，例如 [智]美浦波旁
            var card = new SupportCardName(20001, "[ミッション]", "波旁", 106, 1004);
            Assert.Equal($"[{Localization.Game.I18N_WizSimple}]美浦波旁", card.SimpleName);
        }
    }
}
