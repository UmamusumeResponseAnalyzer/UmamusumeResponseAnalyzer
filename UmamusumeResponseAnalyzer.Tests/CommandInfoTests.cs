using System.Reflection;
using Gallop;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("Database")]
    public class CommandInfoTests
    {
        public CommandInfoTests()
        {
            EnsureConfigInitialized();

            Database.Names = new NameManager(
            [
                new SupportCardName(30001, "速卡", 101, 1001),
                new SupportCardName(30002, "力卡", 102, 1002),
                new SupportCardName(30003, "友卡", 0,   1003),
                new SupportCardName(30137, "神团", 0,   1004),
                new SupportCardName(30067, "皇团", 101, 1005),
                new BaseName(101, "理事长"),
                new BaseName(1001, "训练员"),
            ]);
        }

        static void EnsureConfigInitialized()
        {
            var cfgType = typeof(Config);
            var currentProp = cfgType.GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (currentProp.GetValue(null) != null) return;

            var yamlType = cfgType.Assembly.GetType("UmamusumeResponseAnalyzer.YamlConfig")!;
            var yaml = Activator.CreateInstance(yamlType)!;
            foreach (var prop in yamlType.GetProperties())
                prop.SetValue(yaml, Activator.CreateInstance(prop.PropertyType));
            currentProp.SetValue(null, yaml);
        }

        static SingleModeCheckEventResponse.CommonResponse MakeResp(
            SingleModeSupportCard[] supportCards,
            EvaluationInfo[] evaluations,
            SingleModeCommandInfo[] commands,
            TrainingLevelInfo[]? trainingLevels = null,
            int[]? charaEffectIds = null) => new()
            {
                chara_info = new SingleModeChara
                {
                    card_id = 100101,
                    scenario_id = 1,
                    turn = 1,
                    support_card_array = supportCards,
                    evaluation_info_array = evaluations,
                    training_level_info_array = trainingLevels ?? [],
                    chara_effect_id_array = charaEffectIds ?? [],
                },
                home_info = new SingleModeHomeInfo
                {
                    command_info_array = commands,
                },
                unchecked_event_array = [],
            };

        static SingleModeCommandInfo Command(int commandId, int[] partners, int[]? tips = null) => new()
        {
            command_id = commandId,
            training_partner_array = partners,
            tips_event_partner_array = tips ?? [],
        };

        static EvaluationInfo Eval(int targetId, int evaluation) => new()
        {
            target_id = targetId,
            evaluation = evaluation,
        };

        static SingleModeSupportCard Card(int position, int cardId) => new()
        {
            position = position,
            support_card_id = cardId,
        };

        static TrainingPartner BuildPartner(
            int position, int cardId, int friendship, int commandId,
            int[]? charaEffectIds = null,
            int[]? tips = null)
        {
            var supportCards = position is >= 1 and <= 6 ? new[] { Card(position, cardId) } : [];
            var command = Command(commandId, [position], tips);
            var resp = MakeResp(
                supportCards,
                [Eval(position, friendship)],
                [command],
                charaEffectIds: charaEffectIds);
            var turn = new TurnInfo(resp);
            return new TrainingPartner(turn, position, command);
        }

        [Fact]
        public void Shining_True_WhenFriendship80AndOnSpecialty()
        {
            var partner = BuildPartner(position: 1, cardId: 30001, friendship: 80, commandId: 101);

            Assert.True(partner.Shining);
            Assert.Equal(PartnerPriority.闪, partner.Priority);
            Assert.False(partner.IsNpc);
            Assert.Equal(30001, partner.CardId);
            Assert.Equal(80, partner.Friendship);
        }

        [Fact]
        public void Shining_False_WhenFriendshipBelow80()
        {
            var partner = BuildPartner(position: 1, cardId: 30001, friendship: 79, commandId: 101);

            Assert.False(partner.Shining);
            Assert.Equal(PartnerPriority.羁绊不足, partner.Priority);
        }

        [Fact]
        public void Shining_False_WhenNotOnSpecialty()
        {
            var partner = BuildPartner(position: 1, cardId: 30001, friendship: 100, commandId: 102);

            Assert.False(partner.Shining);
            Assert.Equal(PartnerPriority.默认, partner.Priority);
        }

        [Fact]
        public void FriendCard_PriorityIsFriend_AndNotShiningOnNonMatchingType()
        {
            var partner = BuildPartner(position: 2, cardId: 30003, friendship: 100, commandId: 101);

            Assert.Equal(PartnerPriority.友人, partner.Priority);
            Assert.False(partner.Shining);
            Assert.False(partner.IsNpc);
        }

        [Fact]
        public void Shining_True_ViaTeamCardLinkage()
        {
            var partner = BuildPartner(
                position: 3, cardId: 30067, friendship: 80, commandId: 102,
                charaEffectIds: [101]);

            Assert.True(partner.Shining);
            Assert.Equal(PartnerPriority.闪, partner.Priority);
        }

        [Fact]
        public void Shining_True_ViaFriendTeamCardLinkage_KeepsFriendPriority()
        {
            var partner = BuildPartner(
                position: 4, cardId: 30137, friendship: 80, commandId: 102,
                charaEffectIds: [102]);

            Assert.True(partner.Shining);
            Assert.Equal(PartnerPriority.友人, partner.Priority);
        }

        [Fact]
        public void Shining_False_WhenTeamCardEffectIdMissing()
        {
            var partner = BuildPartner(
                position: 3, cardId: 30067, friendship: 80, commandId: 102,
                charaEffectIds: [999]);

            Assert.False(partner.Shining);
        }

        [Fact]
        public void HintPartner_PrefixesNameWithMarker()
        {
            var partner = BuildPartner(position: 1, cardId: 30001, friendship: 80, commandId: 101, tips: [1]);

            Assert.StartsWith("[red]![/]", partner.Name);
        }

        [Fact]
        public void IsNpc_True_AndKeyNpcPriority_ForChairman()
        {
            var partner = BuildPartner(position: 101, cardId: 0, friendship: 50, commandId: 101);

            Assert.True(partner.IsNpc);
            Assert.Equal(PartnerPriority.关键NPC, partner.Priority);
            Assert.False(partner.Shining);
        }

        [Fact]
        public void IsNpc_True_AndDefaultPriority_ForGuestBeyond1000()
        {
            var partner = BuildPartner(position: 1001, cardId: 0, friendship: 50, commandId: 101);

            Assert.True(partner.IsNpc);
            Assert.Equal(PartnerPriority.默认, partner.Priority);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(6, false)]
        [InlineData(7, true)]
        [InlineData(0, true)]
        public void IsNpc_BoundaryByPosition(int position, bool expectedIsNpc)
        {
            var cardId = position is >= 1 and <= 6 ? 30001 : 0;
            var partner = BuildPartner(position: position, cardId: cardId, friendship: 50, commandId: 101);

            Assert.Equal(expectedIsNpc, partner.IsNpc);
        }

        [Theory]
        [InlineData(1101, 1)]
        [InlineData(1105, 5)]
        [InlineData(605, 5)]
        [InlineData(101, 1)]
        [InlineData(906, 5)]
        public void TrainIndex_MapsCommandIdToOneBasedSlot(int commandId, int expected)
        {
            var info = MakeCommandInfo(commandId);

            Assert.Equal(expected, info.TrainIndex);
        }

        [Fact]
        public void TrainIndex_ZeroWhenCommandIdUnknown()
        {
            var info = MakeCommandInfo(9999);

            Assert.Equal(0, info.TrainIndex);
        }

        [Fact]
        public void TrainIndex_HonorsCustomDictionary()
        {
            var resp = MakeResp([], [], [Command(777, [])]);
            var turn = new TurnInfo(resp);
            var info = new CommandInfo(resp, turn, 777, trainIndexDictionary: new Dictionary<int, int> { [777] = 5 });

            Assert.Equal(6, info.TrainIndex);
        }

        [Fact]
        public void TrainLevel_ReadsFromTrainingLevelInfoArray()
        {
            var resp = MakeResp(
                [], [], [Command(1101, [])],
                trainingLevels: [new TrainingLevelInfo { command_id = 1101, level = 3 }]);
            var turn = new TurnInfo(resp);
            var info = new CommandInfo(resp, turn, 1101);

            Assert.Equal(3, info.TrainLevel);
        }

        [Fact]
        public void TrainLevel_ZeroWhenNoMatchingEntry()
        {
            var resp = MakeResp(
                [], [], [Command(1101, [])],
                trainingLevels: [new TrainingLevelInfo { command_id = 1102, level = 5 }]);
            var turn = new TurnInfo(resp);
            var info = new CommandInfo(resp, turn, 1101);

            Assert.Equal(0, info.TrainLevel);
        }

        [Fact]
        public void TrainingPartners_BuiltAndSortedByPriority()
        {
            var supportCards = new[] { Card(1, 30001), Card(2, 30001) };
            var command = Command(101, [1, 2]);
            var resp = MakeResp(supportCards, [Eval(1, 80), Eval(2, 50)], [command]);
            var turn = new TurnInfo(resp);
            var info = new CommandInfo(resp, turn, 101);

            var partners = info.TrainingPartners.ToList();

            Assert.Equal(2, partners.Count);
            Assert.Equal(PartnerPriority.闪, partners[0].Priority);
            Assert.Equal(PartnerPriority.羁绊不足, partners[1].Priority);
            Assert.True(partners[0].Shining);
            Assert.False(partners[1].Shining);
        }

        [Fact]
        public void TrainingPartners_EmptyWhenNoPartners()
        {
            var command = Command(101, []);
            var resp = MakeResp([], [], [command]);
            var turn = new TurnInfo(resp);
            var info = new CommandInfo(resp, turn, 101);

            Assert.Empty(info.TrainingPartners);
        }

        static CommandInfo MakeCommandInfo(int commandId)
        {
            var resp = MakeResp([], [], [Command(commandId, [])]);
            var turn = new TurnInfo(resp);
            return new CommandInfo(resp, turn, commandId);
        }
    }
}
