using Gallop;
using MessagePack;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// Tier 3：从真实响应构造 <see cref="TurnInfo"/> 领域视图，断言跨整局都成立的不变量。
    /// 场景专用数据由插件直接消费 Gallop DTO，宿主只验证基础回合视图。
    /// </summary>
    public class DomainLogicTests
    {
        static TurnInfo Build(string path) =>
            new(MessagePackSerializer.Deserialize<SingleModeCheckEventResponse>(PacketCorpus.LoadBytes(path))!.data);

        [Theory]
        [MemberData(nameof(PacketCorpus.SingleModeTurnCases), MemberType = typeof(PacketCorpus))]
        public void TurnInfo_InvariantsHold(string? path)
        {
            Assert.SkipWhen(path is null, "无完整单人模式回合语料");

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

        [Fact]
        public void SingleModeRun_CoversMultipleTurnsWithRealStats()
        {
            Assert.SkipUnless(PacketCorpus.SingleModeTurnResponseFiles.Count > 0, "无完整单人模式回合语料");

            var turns = PacketCorpus.SingleModeTurnResponseFiles.Select(Build).ToList();

            // 一整局应跨越多个回合，且至少出现过非零五维总和——证明 stat 字段端到端被正确解析
            Assert.True(turns.Max(t => t.Turn) > turns.Min(t => t.Turn), "应覆盖多个回合");
            Assert.Contains(turns, t => t.TotalStats > 0);
            // 同一局里 scenario 应一致
            Assert.Single(turns.Select(t => t.Scenario).Distinct());
        }
    }
}
