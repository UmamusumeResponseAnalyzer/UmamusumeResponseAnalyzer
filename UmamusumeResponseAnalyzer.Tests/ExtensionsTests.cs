using System.Reflection;
using Gallop;
using Gallop.Mecha; // SingleModeMechaDataSet 在子命名空间 Gallop.Mecha，其余 *_data_set 都在 Gallop 顶层
using Newtonsoft.Json.Linq;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// <see cref="Extensions"/> / <see cref="GallopExtensions"/> 的纯函数单测——不依赖语料 / Database，始终运行。
    /// 期望值均按生产代码实际逻辑推算（见各处注释），不臆造。
    /// </summary>
    public class ExtensionsTests
    {
        // ───────────────────────── ① Replace(byte[], byte[], byte[]) ─────────────────────────

        [Fact]
        public void Replace_Hit_ReplacesMatch()
        {
            // [1,2] → [9]：单处命中
            byte[] result = new byte[] { 0, 1, 2, 3 }.Replace([1, 2], [9]);
            Assert.Equal(new byte[] { 0, 9, 3 }, result);
        }

        [Fact]
        public void Replace_NoMatch_ReturnsOriginalContent()
        {
            // 模式不存在 → 内容逐字节原样复制
            byte[] input = [1, 2, 3, 4];
            byte[] result = input.Replace([7, 8], [9]);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, result);
        }

        [Fact]
        public void Replace_MultipleNonOverlappingHits_AllReplaced()
        {
            // [1,2] 出现两次且不重叠（命中后 i += pattern.Length-1 跳过整段），均被替换
            byte[] result = new byte[] { 1, 2, 1, 2 }.Replace([1, 2], [9]);
            Assert.Equal(new byte[] { 9, 9 }, result);
        }

        [Fact]
        public void Replace_MatchAtStart_Replaced()
        {
            // 模式在首部，替换串比模式长（验证不是等长替换）
            byte[] result = new byte[] { 1, 2, 3 }.Replace([1, 2], [8, 8, 8]);
            Assert.Equal(new byte[] { 8, 8, 8, 3 }, result);
        }

        [Fact]
        public void Replace_MatchAtEnd_Replaced()
        {
            // 模式在尾部：主循环到 i==input.Length-pattern.Length 仍能命中
            byte[] result = new byte[] { 0, 1, 2 }.Replace([1, 2], [9, 9, 9]);
            Assert.Equal(new byte[] { 0, 9, 9, 9 }, result);
        }

        [Fact]
        public void Replace_EmptyPattern_ReturnsInputUnchanged()
        {
            // pattern.Length==0 时直接 return input（同一引用）
            byte[] input = [1, 2, 3];
            byte[] result = input.Replace([], [9]);
            Assert.Same(input, result);
        }

        [Fact]
        public void Replace_PatternLongerThanInput_ReturnsCopyOfInput()
        {
            // 模式比输入长：主循环上界 input.Length-pattern.Length 为负，不进；tail 把 input 整段复制
            byte[] input = [1, 2];
            byte[] result = input.Replace([1, 2, 3], [9]);
            Assert.Equal(new byte[] { 1, 2 }, result);
            Assert.NotSame(input, result); // 这条路径返回的是新建数组，非原引用
        }

        [Fact]
        public void Replace_EmptyInput_ReturnsEmpty()
        {
            byte[] result = Array.Empty<byte>().Replace([1], [9]);
            Assert.Empty(result);
        }

        // ───────────────────────── ② Contains<T>(IEnumerable<T>, Predicate<T>) ─────────────────────────

        [Fact]
        public void Contains_NullSource_ReturnsFalse()
        {
            // list == default 短路
            IEnumerable<int> list = null!;
            Assert.False(list.Contains(_ => true));
        }

        [Fact]
        public void Contains_EmptySource_ReturnsFalse()
        {
            Assert.False(Array.Empty<int>().Contains(_ => true));
        }

        [Fact]
        public void Contains_MatchingElement_ReturnsTrue()
        {
            Assert.True(new[] { 1, 2, 3 }.Contains(x => x == 2));
        }

        [Fact]
        public void Contains_NoMatch_ReturnsFalse()
        {
            Assert.False(new[] { 1, 2, 3 }.Contains(x => x == 99));
        }

        [Fact]
        public void Contains_SkipsDefaultElements_BeforePredicate()
        {
            // 引用类型的 default(null) 元素被 continue 跳过，predicate 不会看到它
            var list = new string?[] { null, "hit" };
            // predicate 仅在 "hit" 上为真；若 null 未被跳过，x.Length 会 NRE —— 用它反证跳过逻辑
            Assert.True(list.Contains(x => x!.Length == 3));
        }

        [Fact]
        public void Contains_AllDefaultElements_ReturnsFalse()
        {
            // 全为 null：每个都被跳过，从不调用 predicate → false
            var list = new string?[] { null, null };
            Assert.False(list.Contains(_ => true));
        }

        // ───────────────────────── ③ IsScenario(SingleModeCheckEventResponse, ScenarioType) ─────────────────────────
        // 多数分支要求 scenario_id 匹配【且】对应 *_data_set 非 null。逐分支构造最小响应。

        static SingleModeCheckEventResponse Event(int scenarioId,
            Action<SingleModeCheckEventResponse.CommonResponse>? configure = null)
        {
            var data = new SingleModeCheckEventResponse.CommonResponse
            {
                chara_info = new SingleModeChara { scenario_id = scenarioId }
            };
            configure?.Invoke(data);
            return new SingleModeCheckEventResponse { data = data };
        }

        [Fact]
        public void IsScenario_Ura_OnlyNeedsScenarioId()
        {
            Assert.True(Event(1).IsScenario(ScenarioType.Ura));
        }

        [Fact]
        public void IsScenario_Aoharu_OnlyNeedsScenarioId()
        {
            Assert.True(Event(2).IsScenario(ScenarioType.Aoharu));
        }

        [Fact]
        public void IsScenario_GrandLive_OnlyNeedsScenarioId()
        {
            Assert.True(Event(3).IsScenario(ScenarioType.GrandLive));
        }

        [Fact]
        public void IsScenario_MakeANewTrack_NeedsPickUpItemInfoArray()
        {
            // scenario_id==4 且 free_data_set.pick_up_item_info_array != null
            var ev = Event(4, d => d.free_data_set = new SingleModeFreeDataSet
            {
                pick_up_item_info_array = []
            });
            Assert.True(ev.IsScenario(ScenarioType.MakeANewTrack));
        }

        [Fact]
        public void IsScenario_MakeANewTrack_NullPickUpArray_False()
        {
            // free_data_set 存在但 pick_up_item_info_array 为 null → 不算 MakeANewTrack
            var ev = Event(4, d => d.free_data_set = new SingleModeFreeDataSet());
            Assert.False(ev.IsScenario(ScenarioType.MakeANewTrack));
        }

        [Fact]
        public void IsScenario_GrandMasters_NeedsVenusDataSet()
        {
            var ev = Event(5, d => d.venus_data_set = new SingleModeVenusDataSet());
            Assert.True(ev.IsScenario(ScenarioType.GrandMasters));
        }

        [Fact]
        public void IsScenario_LArc_NeedsArcDataSet()
        {
            var ev = Event(6, d => d.arc_data_set = new SingleModeArcDataSet());
            Assert.True(ev.IsScenario(ScenarioType.LArc));
        }

        [Fact]
        public void IsScenario_UAF_NeedsSportDataSet()
        {
            var ev = Event(7, d => d.sport_data_set = new SingleModeSportDataSet());
            Assert.True(ev.IsScenario(ScenarioType.UAF));
        }

        [Fact]
        public void IsScenario_Cook_NeedsCookDataSet()
        {
            var ev = Event(8, d => d.cook_data_set = new SingleModeCookDataSet());
            Assert.True(ev.IsScenario(ScenarioType.Cook));
        }

        [Fact]
        public void IsScenario_Mecha_NeedsMechaDataSet()
        {
            var ev = Event(9, d => d.mecha_data_set = new SingleModeMechaDataSet());
            Assert.True(ev.IsScenario(ScenarioType.Mecha));
        }

        [Fact]
        public void IsScenario_Legend_NeedsLegendDataSet()
        {
            var ev = Event(10, d => d.legend_data_set = new SingleModeLegendDataSet());
            Assert.True(ev.IsScenario(ScenarioType.Legend));
        }

        [Fact]
        public void IsScenario_Pioneer_NeedsPioneerDataSet()
        {
            var ev = Event(11, d => d.pioneer_data_set = new SingleModePioneerDataSet());
            Assert.True(ev.IsScenario(ScenarioType.Pioneer));
        }

        [Fact]
        public void IsScenario_Onsen_NeedsOnsenDataSet()
        {
            var ev = Event(12, d => d.onsen_data_set = new SingleModeOnsenDataSet());
            Assert.True(ev.IsScenario(ScenarioType.Onsen));
        }

        [Fact]
        public void IsScenario_DataSetNull_ReturnsFalse()
        {
            // scenario_id 对上但 *_data_set 为 null → false（以 Cook 为代表）
            Assert.False(Event(8).IsScenario(ScenarioType.Cook));
        }

        [Fact]
        public void IsScenario_ScenarioIdMismatch_ReturnsFalse()
        {
            // data_set 齐了但 scenario_id 不匹配 → 仍 false
            var ev = Event(1, d => d.cook_data_set = new SingleModeCookDataSet());
            Assert.False(ev.IsScenario(ScenarioType.Cook));
        }

        [Fact]
        public void IsScenario_Unknown_AlwaysTrue()
        {
            // Unknown 分支无条件 true（不读任何字段）
            Assert.True(Event(0).IsScenario(ScenarioType.Unknown));
            Assert.True(Event(9999).IsScenario(ScenarioType.Unknown));
        }

        [Fact]
        public void IsScenario_Breeders_HitsDefaultBranch_False()
        {
            // ScenarioType.Breeders(13) 在 switch 里没有对应 case → 落入 _ => false
            var ev = Event(13, d => d.breeders_data_set = new SingleModeBreedersDataSet());
            Assert.False(ev.IsScenario(ScenarioType.Breeders));
        }

        // ───────────────────────── ④ AppendValue(PropertyInfo, object?, Dictionary) ─────────────────────────
        // 用一个本地探针类型暴露不同形态的属性，再用反射取 PropertyInfo 喂给被测方法。

        sealed class Probe
        {
            public string Name { get; set; } = "abc";
            public int Count { get; set; } = 42;
            public string? Nothing { get; set; } = null;
            public string Bracketed { get; set; } = "a[b]c";
            public IEnumerable<string> Tags { get; set; } = ["x", "y"];
            public IEnumerable<string> BracketTags { get; set; } = ["a[1]", "b[2]"];
        }

        static PropertyInfo Prop(string name) => typeof(Probe).GetProperty(name)!;

        [Fact]
        public void AppendValue_StringValue_NoDic_FormatsNameColonValue()
        {
            var s = Prop(nameof(Probe.Name)).AppendValue(new Probe());
            Assert.Equal("Name: abc", s);
        }

        [Fact]
        public void AppendValue_NonStringValue_UsesToString()
        {
            var s = Prop(nameof(Probe.Count)).AppendValue(new Probe());
            Assert.Equal("Count: 42", s);
        }

        [Fact]
        public void AppendValue_NullValue_YieldsEmptyValueString()
        {
            // value 为 null → valueString = string.Empty
            var s = Prop(nameof(Probe.Nothing)).AppendValue(new Probe());
            Assert.Equal("Nothing: ", s);
        }

        [Fact]
        public void AppendValue_ScalarBrackets_AreDoubled()
        {
            // 单值里的 [ ] 各翻倍（Spectre.Console 转义）
            var s = Prop(nameof(Probe.Bracketed)).AppendValue(new Probe());
            Assert.Equal("Bracketed: a[[b]]c", s);
        }

        [Fact]
        public void AppendValue_StringEnumerable_JoinedByComma()
        {
            // IEnumerable<string> 走 string.Join(",", ...) 分支
            var s = Prop(nameof(Probe.Tags)).AppendValue(new Probe());
            Assert.Equal("Tags: x,y", s);
        }

        [Fact]
        public void AppendValue_StringEnumerable_EscapesBracketsPerElement()
        {
            var s = Prop(nameof(Probe.BracketTags)).AppendValue(new Probe());
            Assert.Equal("BracketTags: a[[1]],b[[2]]", s);
        }

        [Fact]
        public void AppendValue_WithTranslationDic_UsesTranslatedName()
        {
            var dic = new Dictionary<string, string> { ["Name"] = "名字" };
            var s = Prop(nameof(Probe.Name)).AppendValue(new Probe(), dic);
            Assert.Equal("名字: abc", s);
        }

        [Fact]
        public void AppendValue_WithDic_MissingKey_FallsBackToPropertyName()
        {
            // 字典非 null 但无该键 → translated 为 null → 用 property.Name
            var dic = new Dictionary<string, string> { ["Other"] = "别的" };
            var s = Prop(nameof(Probe.Name)).AppendValue(new Probe(), dic);
            Assert.Equal("Name: abc", s);
        }

        // ───────────────────────── ⑤ HasCharaInfo(JObject?) ─────────────────────────

        [Fact]
        public void HasCharaInfo_Null_False()
        {
            Assert.False(((JObject?)null).HasCharaInfo());
        }

        [Fact]
        public void HasCharaInfo_NoData_False()
        {
            Assert.False(JObject.Parse("""{"x":1}""").HasCharaInfo());
        }

        [Fact]
        public void HasCharaInfo_DataWithoutCharaInfo_False()
        {
            // data 存在但 data.chara_info 不存在；ContainsKey 对非 JObject 的 data 也安全返回 false
            Assert.False(JObject.Parse("""{"data":{"foo":1}}""").HasCharaInfo());
        }

        [Fact]
        public void HasCharaInfo_DataWithCharaInfo_True()
        {
            Assert.True(JObject.Parse("""{"data":{"chara_info":{"x":1}}}""").HasCharaInfo());
        }

        // ───────────────────────── ⑥ ContainsKey(JToken?, string) ─────────────────────────

        [Fact]
        public void ContainsKey_Null_False()
        {
            Assert.False(((JToken?)null).ContainsKey("a"));
        }

        [Fact]
        public void ContainsKey_NonObjectToken_False()
        {
            // JArray 不是 JObject → false（不抛）
            JToken arr = JArray.Parse("[1,2,3]");
            Assert.False(arr.ContainsKey("0"));
        }

        [Fact]
        public void ContainsKey_ObjectWithKey_True()
        {
            JToken jo = JObject.Parse("""{"a":1}""");
            Assert.True(jo.ContainsKey("a"));
        }

        [Fact]
        public void ContainsKey_ObjectWithoutKey_False()
        {
            JToken jo = JObject.Parse("""{"a":1}""");
            Assert.False(jo.ContainsKey("b"));
        }

        // ───────────────────────── ⑦ ToInt(JToken?) ─────────────────────────

        [Fact]
        public void ToInt_Null_ReturnsZero()
        {
            Assert.Equal(0, ((JToken?)null).ToInt());
        }

        [Fact]
        public void ToInt_IntegerToken_ReturnsValue()
        {
            Assert.Equal(7, JToken.Parse("7").ToInt());
        }

        [Fact]
        public void ToInt_NumericString_Converts()
        {
            // ToObject<int>() 可把数字字符串转成 int
            Assert.Equal(42, JToken.Parse("\"42\"").ToInt());
        }

        // ───────────────────────── ⑧ IsNull(JToken?) ─────────────────────────

        [Fact]
        public void IsNull_CSharpNull_True()
        {
            Assert.True(((JToken?)null).IsNull());
        }

        [Fact]
        public void IsNull_JsonNullToken_True()
        {
            // JSON 字面量 null → JTokenType.Null
            Assert.True(JToken.Parse("null").IsNull());
        }

        [Fact]
        public void IsNull_NonNullToken_False()
        {
            Assert.False(JToken.Parse("0").IsNull());
            Assert.False(JToken.Parse("\"\"").IsNull());
        }

        // ───────────────────────── ⑨ GallopExtensions.GetCommandInfoStage(SingleModeCheckEventResponse) ─────────────────────────

        static SingleModeCheckEventResponse StageEvent(int playingState, SingleModeEventInfo[]? events)
        {
            return new SingleModeCheckEventResponse
            {
                data = new SingleModeCheckEventResponse.CommonResponse
                {
                    chara_info = new SingleModeChara { playing_state = playingState },
                    unchecked_event_array = events!
                }
            };
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState1_NullEvents_Returns2()
        {
            // 常规训练：playing_state==1 且无未处理事件（null）
            Assert.Equal(2, StageEvent(1, null).GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState1_EmptyEvents_Returns2()
        {
            // playing_state==1 且事件数组为空（Length==0）
            Assert.Equal(2, StageEvent(1, []).GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState1_WithEvents_Returns0()
        {
            // playing_state==1 但有未处理事件 → 第一分支条件不成立，后续分支要 state==5，最终落 else
            var ev = StageEvent(1, [new SingleModeEventInfo { story_id = 1 }]);
            Assert.Equal(0, ev.GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState5_BuffStory_Returns5()
        {
            // playing_state==5 且含 story_id 400010112（选 buff）
            var ev = StageEvent(5, [new SingleModeEventInfo { story_id = 400010112 }]);
            Assert.Equal(5, ev.GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState5_TeamCardStory_Returns3()
        {
            // playing_state==5 且含 story_id 830241003（选团卡事件），但不含 400010112
            var ev = StageEvent(5, [new SingleModeEventInfo { story_id = 830241003 }]);
            Assert.Equal(3, ev.GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState5_OtherStory_Returns0()
        {
            // playing_state==5 但事件 story_id 既非 buff 也非团卡 → 两个 Any 都 false → else
            var ev = StageEvent(5, [new SingleModeEventInfo { story_id = 12345 }]);
            Assert.Equal(0, ev.GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_PlayingState5_NullEvents_Returns0()
        {
            // 回归：playing_state==5 且 unchecked_event_array 为 null。修复前 ==5 分支裸调 .Any() 会抛 NRE；
            // 修复后与 ==1 分支一致地把 null 视为无事件 → 两个 Any 都 false → else 返回 0（不抛异常）。
            Assert.Equal(0, StageEvent(5, null).GetCommandInfoStage());
        }

        [Fact]
        public void GetCommandInfoStage_OtherPlayingState_Returns0()
        {
            // playing_state 非 1 非 5 → 直接 else
            Assert.Equal(0, StageEvent(0, []).GetCommandInfoStage());
        }
    }
}
