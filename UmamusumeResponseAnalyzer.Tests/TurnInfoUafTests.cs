using Gallop;
using System.Reflection;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// Tier 3：合成 UAF（scenario_id=7）响应，断言 <see cref="TurnInfoUAF"/> 最复杂的派生量。
    /// 聚焦三处痛点：
    /// 1. <c>ActualGainRank / ActualGainRankWithoutBuff</c> —— Shining 加成倍率(×2) + Link 角色 ID 交集 + 搭档人数加成；
    /// 2. <c>SportsByColor</c> 按颜色分组求和得到 Blue/Red/Yellow Level；
    /// 3. <c>ParsedSingleModeSportCommandInfo</c> 从 command_id 拆出 TrainIndex/Color 并跨三处数组取值。
    ///
    /// 期望值全部按生产代码逐行推算，未臆造。被测访问的字段/数组一律构造为非 null。
    /// 因为会 seed 全局静态 <c>Database.Names</c>，加入 "Database" collection 关闭并行。
    /// </summary>
    [Collection("Database")]
    public class TurnInfoUafTests
    {
        // —— Link 角色 ID（与生产代码 TurnInfoUAF.LinkCharacterIds 一致）——
        const int LinkCharaId = 1035;       // 落在 LinkCharacterIds 内
        const int NonLinkCharaId = 1001;    // 不在 LinkCharacterIds 内

        // —— 支援卡类型 → 类型标签（与 SupportCardName.TypeName 一致）——
        // 101=>[速]，102=>[力]。训练 1101 经 ToTrainId 映射到 101=>"[速]"，
        // 故仅 Type=101 的卡名(Nickname 以"[速]"开头)会触发 Shining 判定。
        const int TypeSpeed = 101;   // Nickname => "[速]未知"，含 "[速]" => 可闪
        const int TypePower = 102;   // Nickname => "[力]未知"，不含 "[速]" => 不闪

        // 测试用到的全部支援卡 id（seed 进 Database.Names；id 即 support_card_id 即 CardId）
        const int CardSpeedNonLink = 30001;  // Type=速, 非Link
        const int CardPowerNonLink = 30002;  // Type=力, 非Link
        const int CardSpeedLink = 30003;     // Type=速, Link
        const int CardPowerLink = 30004;     // Type=力, Link

        public TurnInfoUafTests()
        {
            // 触碰任何 Database 成员都会触发其静态构造器，而 Database.cs 的静态字段初始化器读了
            // Config.Updater；测试环境下 Config.Current 为 null 会 NRE。沿用本仓库既有约定
            // （见 ConfigDatabaseTests.DatabaseStaticTableTests）：反射注入一个 YamlConfig 到
            // private static Config.Current（避开会写盘的 Config.Initialize），且仅在尚未初始化时注入。
            var currentProp = typeof(Config).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (currentProp.GetValue(null) is null)
                currentProp.SetValue(null, new YamlConfig
                {
                    Core = new(),
                    Repository = new(),
                    Plugin = new(),
                    Updater = new(),
                    Language = new(),
                    Misc = new(),
                });

            // NameManager 构造时会把 SupportCardName 的 Nickname 重写为 "{TypeName}{原Nickname}"，
            // 默认 Nickname="未知" => 速卡变 "[速]未知"、力卡变 "[力]未知"，正是 Shining 判定的依据。
            // Link 判定只读 GetSupportCard(CardId).CharaId（直接取字段，不二次查名表），故仅需 seed 这 4 张支援卡。
            Database.Names = new NameManager(
            [
                new SupportCardName(CardSpeedNonLink, "速非Link", TypeSpeed, NonLinkCharaId),
                new SupportCardName(CardPowerNonLink, "力非Link", TypePower, NonLinkCharaId),
                new SupportCardName(CardSpeedLink, "速Link", TypeSpeed, LinkCharaId),
                new SupportCardName(CardPowerLink, "力Link", TypePower, LinkCharaId),
            ]);
        }

        /// <summary>一名搭档的描述：位置、所带支援卡 id、羁绊值。</summary>
        sealed record Partner(int Position, int SupportCardId, int Friendship);

        const int Command = 1101; // UAF 特色训练 command_id：[1]='1'=>Blue，[3]='1'=>TrainIndex=1

        /// <summary>
        /// 构造一份最小可用的 UAF 响应。
        /// <paramref name="gainRank"/> 直接决定 turn 级 <c>IsRankGainIncreased</c>：
        /// 生产代码里 <c>IsCurrentRankGainIncreased = GainRank - ActualGainRankWithoutBuff == 3</c>，
        /// 本响应只有一个训练 command，故 turn.IsRankGainIncreased 等价于该 command 的判定。
        /// 想"有 buff"就传 gainRank = 期望 ActualGainRankWithoutBuff + 3；想"无 buff"传相等值(差 0)。
        /// </summary>
        static SingleModeCheckEventResponse.CommonResponse BuildResponse(
            IReadOnlyList<Partner> partners, int gainRank, int sportRank = 7)
        {
            var positions = partners.Select(p => p.Position).ToArray();
            return new SingleModeCheckEventResponse.CommonResponse
            {
                chara_info = new SingleModeChara
                {
                    card_id = 10010101,            // 前4位 1001 => CharacterId>0
                    scenario_id = (int)ScenarioType.UAF, // =7，使 IsScenario(LArc/GrandMasters) 均为 false
                    turn = 1,
                    // 每个搭档位置都要有 evaluation（Evaluations[Position] 是 FrozenDictionary 索引，缺失会抛）
                    evaluation_info_array = partners
                        .Select(p => new EvaluationInfo { target_id = p.Position, evaluation = p.Friendship })
                        .ToArray(),
                    // 非 NPC(位置1~6) 通过 SupportCards[Position] 取 CardId，必须有对应条目
                    support_card_array = partners
                        .Where(p => p.Position is >= 1 and <= 6)
                        .Select(p => new SingleModeSupportCard { position = p.Position, support_card_id = p.SupportCardId })
                        .ToArray(),
                    training_level_info_array = [],   // FirstOrDefault => TrainLevel=0
                    chara_effect_id_array = [],       // 不触发 30137/30067/30081 团队卡强制闪
                },
                home_info = new SingleModeHomeInfo
                {
                    // 基类 CommandInfo 用 home_info 取搭档；本训练 command 必须在此出现
                    command_info_array =
                    [
                        new SingleModeCommandInfo
                        {
                            command_id = Command,
                            training_partner_array = positions,
                            tips_event_partner_array = [],
                        }
                    ],
                },
                sport_data_set = new SingleModeSportDataSet
                {
                    // TrainingArray 来源；同时 ParsedSingleModeSportCommandInfo 会 First(command_id==Command) 取 sport_rank。
                    // 颜色由 command_id 第[1]位决定：1=Blue,2=Red,3=Yellow（1101=>Blue）。
                    // 被测 command 是 Blue(1101)；另补一个 Red(1201) 一个 Yellow(1301) 占位，
                    // 否则 TurnInfoUAF 构造时 SportsByColor[Red]/[Yellow] 索引缺键会抛（生产代码无防御）。
                    // 这两条仅入 training_array、不入 command_info_array，故不会变成被解析的 command、也不污染 ActualGainRank。
                    training_array =
                    [
                        new SingleModeSportTraining { command_id = Command, sport_rank = sportRank, command_type = 1 },
                        new SingleModeSportTraining { command_id = 1201, sport_rank = 0, command_type = 1 }, // Red 占位
                        new SingleModeSportTraining { command_id = 1301, sport_rank = 0, command_type = 1 }, // Yellow 占位
                    ],
                    // CommandInfoArray 来源：Where(command_id>1000)。TrainIndex=1 => 索引 [0]
                    command_info_array =
                    [
                        new SingleModeSportCommandInfo
                        {
                            command_id = Command,
                            gain_sport_rank_array =
                            [
                                new SingleModeSportCommandInfo.SingleModeSportGainRank { command_id = Command, gain_rank = gainRank },
                            ],
                        }
                    ],
                    item_id_array = [],
                },
            };
        }

        // ============== ActualGainRank：Shining × Link 全组合 ==============
        // 统一一名搭档(位置1) + partnerAdd(1人)=1，故 ActualGainRankWithoutBuff = shining*(3+1) + linkCount。
        //   无闪无Link : 1*(3+1)+0 = 4
        //   有闪无Link : 2*(3+1)+0 = 8
        //   无闪有Link : 1*(3+1)+1 = 5
        //   有闪有Link : 2*(3+1)+1 = 9
        // 闪：Type=速(Nickname 含"[速]")且 Friendship>=80；不闪：换力卡或羁绊<80。
        // Link：所带卡 CharaId ∈ LinkCharacterIds。

        [Theory]
        // shining?, hasLink?, 期望 ActualGainRankWithoutBuff
        [InlineData(false, false, 4)]
        [InlineData(true, false, 8)]
        [InlineData(false, true, 5)]
        [InlineData(true, true, 9)]
        public void ActualGainRank_NoBuff_MatchesShiningAndLinkMatrix(bool shining, bool hasLink, int expected)
        {
            var card = (shining, hasLink) switch
            {
                (true, false) => CardSpeedNonLink,
                (true, true) => CardSpeedLink,
                (false, false) => CardPowerNonLink, // 力卡：名字不含"[速]"，即便羁绊满也不闪
                (false, true) => CardPowerLink,
            };
            // 羁绊一律给满(100)：闪与否完全由卡名是否含"[速]"区分(力卡不闪/速卡闪)，单维隔离
            var partners = new[] { new Partner(Position: 1, SupportCardId: card, Friendship: 100) };

            // 无 buff：gainRank == ActualGainRankWithoutBuff（差 0 != 3）=> IsRankGainIncreased=false
            var turn = new TurnInfoUAF(BuildResponse(partners, gainRank: expected));
            var cmd = turn.CommandInfoArray.Single();

            Assert.False(turn.IsRankGainIncreased);
            Assert.Equal(expected, cmd.ActualGainRank); // 无 buff 时 == ActualGainRankWithoutBuff
        }

        [Theory]
        [InlineData(false, false, 4)]
        [InlineData(true, false, 8)]
        [InlineData(false, true, 5)]
        [InlineData(true, true, 9)]
        public void ActualGainRank_WithBuff_AddsThree(bool shining, bool hasLink, int withoutBuff)
        {
            var card = (shining, hasLink) switch
            {
                (true, false) => CardSpeedNonLink,
                (true, true) => CardSpeedLink,
                (false, false) => CardPowerNonLink,
                (false, true) => CardPowerLink,
            };
            var partners = new[] { new Partner(Position: 1, SupportCardId: card, Friendship: 100) };

            // 有 buff：gainRank = withoutBuff + 3（差恰为 3）=> IsCurrentRankGainIncreased => IsRankGainIncreased=true
            var turn = new TurnInfoUAF(BuildResponse(partners, gainRank: withoutBuff + 3));
            var cmd = turn.CommandInfoArray.Single();

            Assert.True(turn.IsRankGainIncreased);
            Assert.Equal(withoutBuff + 3, cmd.ActualGainRank); // 有 buff 时 == ActualGainRankWithoutBuff + 3
        }

        // ============== Shining 不被低羁绊误触发 ==============
        [Fact]
        public void Shining_RequiresFriendshipAtLeast80_OtherwiseNoMultiplier()
        {
            // 速卡(Nickname 含"[速]")但羁绊 79 < 80 => 不闪 => shining=1 => 1*(3+1)+0 = 4
            var partners = new[] { new Partner(Position: 1, SupportCardId: CardSpeedNonLink, Friendship: 79) };
            var turn = new TurnInfoUAF(BuildResponse(partners, gainRank: 4));

            Assert.False(turn.IsRankGainIncreased);
            Assert.Equal(4, turn.CommandInfoArray.Single().ActualGainRank);
        }

        // ============== 搭档人数加成 partnerAdd ==============
        // partnerAdd 开关：0=>0,1=>1,2=>2,3=>2,4=>3,5=>3。全程不闪、无 Link 以隔离该维度。
        // 用力卡(不闪) + 非Link，故 ActualGainRankWithoutBuff = 1*(3+partnerAdd)。
        [Theory]
        [InlineData(1, 4)] // 1人 => add1 => 3+1=4
        [InlineData(2, 5)] // 2人 => add2 => 3+2=5
        [InlineData(3, 5)] // 3人 => add2 => 3+2=5
        [InlineData(4, 6)] // 4人 => add3 => 3+3=6
        [InlineData(5, 6)] // 5人 => add3 => 3+3=6
        public void ActualGainRankWithoutBuff_ScalesWithSupportPartnerCount(int supportCount, int expected)
        {
            // 位置 1..supportCount 各放一张力非Link卡（位置1~6 都算 support）
            var partners = Enumerable.Range(1, supportCount)
                .Select(pos => new Partner(pos, CardPowerNonLink, Friendship: 100))
                .ToArray();
            var turn = new TurnInfoUAF(BuildResponse(partners, gainRank: expected)); // 无 buff

            Assert.False(turn.IsRankGainIncreased);
            Assert.Equal(expected, turn.CommandInfoArray.Single().ActualGainRank);
        }

        // ============== NPC 搭档不计入 supports、但仍参与 Link 交集判定的边界 ==============
        [Fact]
        public void NpcPartner_NotCountedAsSupport_AndContributesNoLink()
        {
            // 位置 1：速非Link、闪；位置 100：NPC(理事长档)，无 CardId(默认0)。
            // supports 仅位置1 => partnerAdd=1；shining=2；links：NPC 的 GetSupportCard(0)=>CharaId=int.MinValue 不入交集。
            // => 2*(3+1)+0 = 8
            var partners = new[]
            {
                new Partner(Position: 1, SupportCardId: CardSpeedNonLink, Friendship: 100),
                new Partner(Position: 100, SupportCardId: 0, Friendship: 50), // NPC：位置不在1~6
            };
            var turn = new TurnInfoUAF(BuildResponse(partners, gainRank: 8));

            Assert.False(turn.IsRankGainIncreased);
            Assert.Equal(8, turn.CommandInfoArray.Single().ActualGainRank);
        }

        // ============== SportsByColor 分组求和 ==============
        [Fact]
        public void SportsByColor_SumsRanksPerColor()
        {
            // 仅测分组求和：command_info_array 全部 <=1000 => CommandInfoArray 为空、不走搭档逻辑。
            // 颜色取 command_id 第[1]位（即第二位数字）：'1'=>Blue,'2'=>Red,'3'=>Yellow。
            // 注意是第二位而非首位：1101=>Blue, 1201=>Red, 1301=>Yellow。
            // Blue: 1101(3)+1102(4)=7  Red: 1201(5)  Yellow: 1301(2)+1302(6)=8
            var resp = new SingleModeCheckEventResponse.CommonResponse
            {
                chara_info = new SingleModeChara
                {
                    card_id = 10010101,
                    scenario_id = (int)ScenarioType.UAF,
                    turn = 1,
                    evaluation_info_array = [],
                    support_card_array = [],
                    training_level_info_array = [],
                    chara_effect_id_array = [],
                },
                home_info = new SingleModeHomeInfo { command_info_array = [] },
                sport_data_set = new SingleModeSportDataSet
                {
                    training_array =
                    [
                        new SingleModeSportTraining { command_id = 1101, sport_rank = 3 }, // Blue
                        new SingleModeSportTraining { command_id = 1102, sport_rank = 4 }, // Blue
                        new SingleModeSportTraining { command_id = 1201, sport_rank = 5 }, // Red
                        new SingleModeSportTraining { command_id = 1301, sport_rank = 2 }, // Yellow
                        new SingleModeSportTraining { command_id = 1302, sport_rank = 6 }, // Yellow
                    ],
                    // 全 <=1000：被 Where(command_id>1000) 过滤掉，CommandInfoArray 为空
                    command_info_array = [new SingleModeSportCommandInfo { command_id = 101, gain_sport_rank_array = [] }],
                    item_id_array = [],
                },
            };

            var turn = new TurnInfoUAF(resp);

            Assert.Empty(turn.CommandInfoArray);
            Assert.False(turn.IsRankGainIncreased);
            Assert.Equal(7, turn.BlueLevel);
            Assert.Equal(5, turn.RedLevel);
            Assert.Equal(8, turn.YellowLevel);
            // 分组桶内元素数
            Assert.Equal(2, turn.SportsByColor[TurnInfoUAF.SportColor.Blue].Count);
            Assert.Single(turn.SportsByColor[TurnInfoUAF.SportColor.Red]);
            Assert.Equal(2, turn.SportsByColor[TurnInfoUAF.SportColor.Yellow].Count);
        }

        // ============== ParsedSingleModeSportCommandInfo 字段解析 ==============
        [Fact]
        public void ParsedSportCommand_ParsesColorIndexRankAndGains()
        {
            var partners = new[] { new Partner(Position: 1, SupportCardId: CardPowerNonLink, Friendship: 100) };
            // 无 buff（gainRank == withoutBuff=4）；sport_rank 给 7 验证 SportRank 取值
            var turn = new TurnInfoUAF(BuildResponse(partners, gainRank: 4, sportRank: 7));
            var cmd = turn.CommandInfoArray.Single();

            Assert.Equal(Command, cmd.CommandId);
            Assert.Equal(1, cmd.TrainIndex);                       // command_id[3]='1'
            Assert.Equal(TurnInfoUAF.SportColor.Blue, cmd.Color);  // command_id[1]='1' => Blue
            Assert.Equal(7, cmd.SportRank);                        // 取自 training_array
            Assert.Equal(4, cmd.GainRank);                         // 取自 gain_sport_rank_array
            Assert.Equal(4, cmd.TotalGainRank);                    // 该 train 槽位 gain_rank 之和（仅一项）
        }

        // ============== AvailableTalkCount：item_id_array 中 id==6 的计数 ==============
        [Fact]
        public void AvailableTalkCount_CountsItemId6()
        {
            var resp = new SingleModeCheckEventResponse.CommonResponse
            {
                chara_info = new SingleModeChara
                {
                    card_id = 10010101,
                    scenario_id = (int)ScenarioType.UAF,
                    turn = 1,
                    evaluation_info_array = [],
                    support_card_array = [],
                    training_level_info_array = [],
                    chara_effect_id_array = [],
                },
                home_info = new SingleModeHomeInfo { command_info_array = [] },
                sport_data_set = new SingleModeSportDataSet
                {
                    // 三色齐备（1101=Blue,1201=Red,1301=Yellow），避免 BlueLevel/RedLevel/YellowLevel 索引缺键抛出
                    training_array =
                    [
                        new SingleModeSportTraining { command_id = 1101, sport_rank = 1 },
                        new SingleModeSportTraining { command_id = 1201, sport_rank = 1 },
                        new SingleModeSportTraining { command_id = 1301, sport_rank = 1 },
                    ],
                    command_info_array = [new SingleModeSportCommandInfo { command_id = 101, gain_sport_rank_array = [] }],
                    item_id_array = [6, 6, 3, 6, 1], // 三个 6
                },
            };

            var turn = new TurnInfoUAF(resp);
            Assert.Equal(3, turn.AvailableTalkCount);
        }
    }
}
