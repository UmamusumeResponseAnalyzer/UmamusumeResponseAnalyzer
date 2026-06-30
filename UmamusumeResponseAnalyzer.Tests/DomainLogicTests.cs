using Gallop;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// Tier 3：从真实响应构造 <see cref="TurnInfo"/> 领域视图，断言跨整局都成立的不变量。
    /// 这是最贴近实际消费方式的回归——协议字段一旦错位（如 stat 不再映射），这里会立刻暴露。
    /// </summary>
    public class DomainLogicTests
    {
        static TurnInfo Build(string path) =>
            new(PacketCorpus.LoadJObject(path).ToObject<SingleModeCheckEventResponse>()!.data);

        [Theory]
        [MemberData(nameof(PacketCorpus.SingleModeCases), MemberType = typeof(PacketCorpus))]
        public void TurnInfo_InvariantsHold(string? path)
        {
            Assert.SkipWhen(path is null, "无单人模式语料");

            var turn = Build(path!);

            Assert.True(turn.CharacterId > 0, "CharacterId 由 card_id 前 4 位得出，应 > 0");
            Assert.True((int)turn.Scenario >= 1, "scenario_id 应 >= 1");
            Assert.Equal(5, turn.Stats.Length);
            Assert.All(turn.Stats, s => Assert.True(s >= 0, "五维应非负"));
            Assert.Equal(5, turn.StatsRevised.Length);
            Assert.True(turn.Turn >= 0);
            Assert.True(turn.Vital >= 0);
            Assert.True(turn.MaxVital >= 0);
        }

        [Theory]
        [MemberData(nameof(PacketCorpus.SingleModeCases), MemberType = typeof(PacketCorpus))]
        public void TurnInfo_ScenarioDetectionRoundTrips(string? path)
        {
            Assert.SkipWhen(path is null, "无单人模式语料");

            var turn = Build(path!);
            // IsScenario 会按当前场景反射构造对应 TurnInfo——应不抛且与 Scenario 自洽
            Assert.True(turn.IsScenario(turn.Scenario));
        }

        [Fact]
        public void SingleModeRun_CoversMultipleTurnsWithRealStats()
        {
            Assert.SkipUnless(PacketCorpus.SingleModeResponseFiles.Count > 0, "无单人模式语料");

            var turns = PacketCorpus.SingleModeResponseFiles.Select(Build).ToList();

            // 一整局应跨越多个回合，且至少出现过非零五维总和——证明 stat 字段端到端被正确解析
            Assert.True(turns.Max(t => t.Turn) > turns.Min(t => t.Turn), "应覆盖多个回合");
            Assert.Contains(turns, t => t.TotalStats > 0);
            // 同一局里 scenario 应一致
            Assert.Single(turns.Select(t => t.Scenario).Distinct());
        }
    }
}
