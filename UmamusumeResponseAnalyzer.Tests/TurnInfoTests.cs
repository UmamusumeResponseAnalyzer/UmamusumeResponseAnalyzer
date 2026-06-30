using Gallop;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    public class TurnInfoTests
    {
        static SingleModeChara MakeChara(
            int cardId = 100502,
            int scenarioId = 1,
            int turn = 1,
            int speed = 100, int stamina = 200, int power = 300, int guts = 400, int wiz = 500,
            int maxSpeed = 1000, int maxStamina = 1000, int maxPower = 1000, int maxGuts = 1000, int maxWiz = 1000,
            int vital = 70, int maxVital = 100, int motivation = 3) => new()
            {
                card_id = cardId,
                scenario_id = scenarioId,
                turn = turn,
                speed = speed,
                stamina = stamina,
                power = power,
                guts = guts,
                wiz = wiz,
                max_speed = maxSpeed,
                max_stamina = maxStamina,
                max_power = maxPower,
                max_guts = maxGuts,
                max_wiz = maxWiz,
                vital = vital,
                max_vital = maxVital,
                motivation = motivation,
                support_card_array = [],
                evaluation_info_array = [],
                training_level_info_array = [],
                chara_effect_id_array = [],
            };

        static SingleModeCheckEventResponse.CommonResponse MakeResp(
            SingleModeChara chara,
            SingleModeHomeInfo? home = null,
            SingleModeEventInfo[]? events = null) => new()
            {
                chara_info = chara,
                home_info = home ?? new SingleModeHomeInfo
                {
                    command_info_array = [],
                    free_continue_time = 0,
                },
                unchecked_event_array = events ?? [],
            };

        [Theory]
        [InlineData(1200, 1200)]
        [InlineData(1201, 1202)]
        [InlineData(1300, 1400)]
        [InlineData(1000, 1000)]
        [InlineData(0, 0)]
        public void ReviseOver1200_AppliedToSpeedRevised(int raw, int expected)
        {
            var turn = new TurnInfo(MakeResp(MakeChara(speed: raw)));

            Assert.Equal(expected, turn.SpeedRevised);
        }

        [Fact]
        public void StatsRevised_AppliesFormulaPerStat()
        {
            var turn = new TurnInfo(MakeResp(
                MakeChara(speed: 1300, stamina: 1200, power: 1201, guts: 600, wiz: 2000)));

            Assert.Equal([1400, 1200, 1202, 600, 2800], turn.StatsRevised);
        }

        [Fact]
        public void MaxStatsRevised_AppliesFormulaPerStat()
        {
            var turn = new TurnInfo(MakeResp(MakeChara(
                maxSpeed: 1250, maxStamina: 1200, maxPower: 800, maxGuts: 1300, maxWiz: 1100)));

            Assert.Equal([1300, 1200, 800, 1400, 1100], turn.MaxStatsRevised);
        }

        [Fact]
        public void CharacterId_TakesFirstFourDigitsOfCardId()
        {
            var turn = new TurnInfo(MakeResp(MakeChara(cardId: 100502)));

            Assert.Equal(1005, turn.CharacterId);
        }

        [Fact]
        public void Stats_IsSpeedStaminaPowerGutsWiz_InThatOrder()
        {
            var turn = new TurnInfo(MakeResp(
                MakeChara(speed: 11, stamina: 22, power: 33, guts: 44, wiz: 55)));

            Assert.Equal([11, 22, 33, 44, 55], turn.Stats);
        }

        [Fact]
        public void TotalStats_UsesRevisedValues()
        {
            var turn = new TurnInfo(MakeResp(
                MakeChara(speed: 1300, stamina: 200, power: 300, guts: 400, wiz: 500)));

            Assert.Equal(2800, turn.TotalStats);
        }

        [Theory]
        [InlineData(1, 1, 1, "前半")]
        [InlineData(2, 1, 1, "后半")]
        [InlineData(24, 1, 12, "后半")]
        [InlineData(25, 2, 1, "前半")]
        [InlineData(48, 2, 12, "后半")]
        [InlineData(49, 3, 1, "前半")]
        public void Year_Month_HalfMonth_DerivedFromTurn(int turn, int year, int month, string half)
        {
            var info = new TurnInfo(MakeResp(MakeChara(turn: turn)));

            Assert.Equal(year, info.Year);
            Assert.Equal(month, info.Month);
            Assert.Equal(half, info.HalfMonth);
        }

        [Fact]
        public void Turn_VitalAndMaxVital_PassThrough()
        {
            var turn = new TurnInfo(MakeResp(MakeChara(turn: 37, vital: 65, maxVital: 120)));

            Assert.Equal(37, turn.Turn);
            Assert.Equal(65, turn.Vital);
            Assert.Equal(120, turn.MaxVital);
        }

        [Theory]
        [InlineData(1, ScenarioType.Ura)]
        [InlineData(6, ScenarioType.LArc)]
        [InlineData(8, ScenarioType.Cook)]
        [InlineData(13, ScenarioType.Breeders)]
        public void Scenario_CastFromScenarioId(int scenarioId, ScenarioType expected)
        {
            var turn = new TurnInfo(MakeResp(MakeChara(scenarioId: scenarioId)));

            Assert.Equal(expected, turn.Scenario);
        }

        [Fact]
        public void SupportCards_MapsPositionToSupportCardId()
        {
            var chara = MakeChara();
            chara.support_card_array =
            [
                new SingleModeSupportCard { position = 1, support_card_id = 30001 },
                new SingleModeSupportCard { position = 2, support_card_id = 30002 },
            ];

            var turn = new TurnInfo(MakeResp(chara));

            Assert.Equal(30001, turn.SupportCards[1]);
            Assert.Equal(30002, turn.SupportCards[2]);
        }

        [Fact]
        public void Evaluations_MapsTargetIdToEvaluationInfo()
        {
            var chara = MakeChara();
            chara.evaluation_info_array =
            [
                new EvaluationInfo { target_id = 1, evaluation = 80 },
                new EvaluationInfo { target_id = 101, evaluation = 35 },
            ];

            var turn = new TurnInfo(MakeResp(chara));

            Assert.Equal(80, turn.Evaluations[1].evaluation);
            Assert.Equal(35, turn.Evaluations[101].evaluation);
        }

        [Fact]
        public void IsFreeContinueAvailable_UsesHomeInfoUnixTime()
        {
            var turn = new TurnInfo(MakeResp(MakeChara(), new SingleModeHomeInfo
            {
                command_info_array = [],
                free_continue_time = 0,
            }));

            Assert.True(turn.IsFreeContinueAvailable);
        }

        [Fact]
        public void IsGoldenSuccession_TrueWhenUncheckedEventHasEffectType2()
        {
            var turn = new TurnInfo(MakeResp(MakeChara(), events:
            [
                new SingleModeEventInfo
                {
                    succession_event_info = new SingleModeSuccessionEventInfo { effect_type = 1 },
                },
                new SingleModeEventInfo
                {
                    succession_event_info = new SingleModeSuccessionEventInfo { effect_type = 2 },
                },
            ]));

            Assert.True(turn.IsGoldenSuccession);
        }
    }
}
