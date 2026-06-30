using System.Reflection;
using Gallop;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// Tier 2：针对 <see cref="CommandInfo"/> 与 <see cref="TrainingPartner"/> 的纯逻辑单测。
    /// 不依赖真实抓包，全部用合成 Gallop 模型 + seed 过的 <see cref="Database.Names"/> 驱动，
    /// 把 Shining 的各条判定分支逐一隔离出来断言精确结果。
    ///
    /// 关键事实（读源码得出，期望值据此推算）：
    /// 1. <see cref="SupportCardName"/> 的构造签名是 (id, name, type, charaId)，第 2 个参数 name 设的是
    ///    BaseName.Name（本名），不是 Nickname。Nickname 是独立自动属性，默认 "未知"（Name.cs:22），
    ///    本测试从不给它赋值。<see cref="NameManager"/> 构造时只对 Nickname 加 TypeName 前缀
    ///    （101→"[速]"、105→"[耐]"、102→"[力]"、103→"[根]"、106→"[智]"、0→"[友]"，NameManager.cs:19）。
    ///    故 seed 一张 Type=101 的卡（其 Nickname 取默认 "未知"），读出来的 Nickname 是 "[速]未知"。
    /// 2. TrainingPartner 先对 Nickname 做 EscapeMarkup（'['→"[[", ']'→"]]"），"[速]未知" 变成
    ///    "[[速]]未知"。但 Shining 判定用的 Name.Contains("[速]") 仍为 true（子串 "[速]" 落在 "[[速]]" 内）。
    ///    所有 Shining 断言只依赖这个类型 token，与 token 后面那段文字（"未知"）无关。
    /// 3. 得意位判定走 ToTrainId[command_id] 映射：command_id 必须是该字典的 key（否则索引器抛异常），
    ///    且其值（必为 101/105/102/103/106 之一）经 switch 选出对应类型 token 去 Contains。
    /// 4. <see cref="TurnInfo.Evaluations"/> 以 target_id 为 key；TrainingPartner 用 Evaluations[Position]
    ///    取 Friendship，故每个被测 Position 都必须在 evaluation_info_array 里有对应条目，否则索引器抛异常。
    /// </summary>
    [Collection("Database")]
    public class CommandInfoTests
    {
        public CommandInfoTests()
        {
            // Database 的静态 .cctor 在初始化字段时读 Config.Updater（Config.cs:15）；若 Config 未初始化，
            // 首次触碰 Database（含 set Database.Names）会因 Config.Current==null 抛 NRE。
            // 生产里由 Config.Initialize() 读/写真实 config.yaml 完成初始化，单测环境无此文件、该方法又是 internal，
            // 故反射注入一个全字段非 null 的 YamlConfig 到私有 Config.Current，最小满足 .cctor 的前置条件。
            // 必须在任何 Database 访问之前执行。
            EnsureConfigInitialized();

            // seed 一批名字：被测 CardId / NPC Position 都要能查到，否则 GetSupportCard/GetCharacter 走 null 分支。
            // 注意：第 2 个参数是本名(Name)，不是 Nickname；Nickname 取默认 "未知"，由 NameManager 按 Type 前缀注入
            // 类型 token。Shining 只看类型 token，故下面注的 Nickname 都是 "[token]未知"。
            Database.Names = new NameManager(
            [
                new SupportCardName(30001, "速卡", 101, 1001), // → Nickname "[速]未知"（Type=101→[速]）
                new SupportCardName(30002, "力卡", 102, 1002), // → Nickname "[力]未知"（Type=102→[力]）
                new SupportCardName(30003, "友卡", 0,   1003), // → Nickname "[友]未知"（Type=0→[友]）
                new SupportCardName(30137, "神团", 0,   1004), // 三女神团队卡，Type=0 即 [友] → Nickname "[友]未知"
                new SupportCardName(30067, "皇团", 101, 1005), // 皇家团队卡，给个速 token 便于断言 Priority=闪 → "[速]未知"
                new BaseName(101, "理事长"),                    // 100~1000 区间 NPC（关键NPC）
            ]);
        }

        /// <summary>
        /// 反射把一个全字段非 null 的 <c>YamlConfig</c> 塞进 <c>Config</c> 的私有静态 <c>Current</c> 属性，
        /// 使后续 <see cref="Database"/> 的静态构造不因 <c>Config.Updater==null</c> 抛 NRE。幂等。
        /// </summary>
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

        // ---- 合成工具：构造一个最小可用的 CommonResponse ----------------------------------------

        /// <summary>
        /// 造一个 CommonResponse。training/command 数组与 evaluation 数组由调用方按需补齐。
        /// scenarioId 默认 1（非 GM、非凯旋门），保证 IsArcPartner/GM 分支因 && 短路而不触发。
        /// </summary>
        static SingleModeCheckEventResponse.CommonResponse MakeResp(
            SingleModeSupportCard[] supportCards,
            EvaluationInfo[] evaluations,
            SingleModeCommandInfo[] commands,
            int scenarioId = 1,
            TrainingLevelInfo[]? trainingLevels = null,
            int[]? charaEffectIds = null,
            SingleModeVenusActiveSpiritEffect[]? venusActiveSpirits = null)
        {
            var resp = new SingleModeCheckEventResponse.CommonResponse
            {
                chara_info = new SingleModeChara
                {
                    card_id = 100101,
                    scenario_id = scenarioId,
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
            };
            // GM 分支会解引用 venus_data_set；scenarioId==5 时必须非 null
            if (scenarioId == 5)
            {
                resp.venus_data_set = new SingleModeVenusDataSet
                {
                    venus_spirit_active_effect_info_array = venusActiveSpirits ?? [],
                };
            }
            return resp;
        }

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

        /// <summary>构造单个 TrainingPartner：把它包进一个只含该 partner 的 command 里，走真实构造路径。</summary>
        static TrainingPartner BuildPartner(
            int position, int cardId, int friendship, int commandId, int scenarioId = 1,
            int[]? charaEffectIds = null, SingleModeVenusActiveSpiritEffect[]? venusActiveSpirits = null)
        {
            var supportCards = position is >= 1 and <= 6 ? new[] { Card(position, cardId) } : [];
            var command = Command(commandId, [position]);
            var resp = MakeResp(
                supportCards,
                [Eval(position, friendship)],
                [command],
                scenarioId,
                charaEffectIds: charaEffectIds,
                venusActiveSpirits: venusActiveSpirits);
            var turn = new TurnInfo(resp);
            return new TrainingPartner(turn, position, command);
        }

        // ---- TrainingPartner.Shining 分支 -------------------------------------------------------

        [Fact]
        public void Shining_True_WhenFriendship80AndOnSpecialty()
        {
            // 速卡(30001→Nickname "[速]未知")在速训练(command_id=101→ToTrainId=101→"[速]")上，羁绊=80
            var p = BuildPartner(position: 1, cardId: 30001, friendship: 80, commandId: 101);

            Assert.True(p.Shining);
            Assert.Equal(PartnerPriority.闪, p.Priority);
            Assert.False(p.IsNpc);
            Assert.Equal(30001, p.CardId);
            Assert.Equal(80, p.Friendship);
        }

        [Fact]
        public void Shining_False_WhenFriendshipBelow80()
        {
            // 同一张速卡、同样在速训练上，但羁绊 79 < 80 → 不闪，且判定为羁绊不足
            var p = BuildPartner(position: 1, cardId: 30001, friendship: 79, commandId: 101);

            Assert.False(p.Shining);
            Assert.Equal(PartnerPriority.羁绊不足, p.Priority);
            Assert.False(p.IsNpc);
        }

        [Fact]
        public void Shining_False_WhenNotOnSpecialty()
        {
            // 速卡羁绊充足(100)，但放在力训练上(command_id=102→ToTrainId=102→"[力]")，
            // 速卡 Name 不含 "[力]" → 不闪。羁绊≥80 且非友人 → Priority 保持默认。
            var p = BuildPartner(position: 1, cardId: 30001, friendship: 100, commandId: 102);

            Assert.False(p.Shining);
            Assert.Equal(PartnerPriority.默认, p.Priority);
        }

        [Fact]
        public void FriendCard_PriorityIsFriend_AndNotShiningOnNonMatchingType()
        {
            // 友人卡(30003→Nickname "[友]未知")。Name.Contains("[友]") 命中 → Priority=友人。
            // 放在速训练上：友人卡名不含 "[速]" → Shining=false，Priority 仍为友人。
            var p = BuildPartner(position: 2, cardId: 30003, friendship: 100, commandId: 101);

            Assert.Equal(PartnerPriority.友人, p.Priority);
            Assert.False(p.Shining);
            Assert.False(p.IsNpc);
        }

        [Fact]
        public void Shining_True_ViaGrandMastersBranch()
        {
            // GM 杯分支：scenario=5(GrandMasters)，venus 激活精灵含 {chara_id=9042, effect_group_id=421}，
            // 且卡名带任一属性 token。速卡放在力训练(command_id=102)上——常规得意判定为 false，
            // 单靠 GM 分支把 Shining 抬成 true，从而隔离该分支。
            var p = BuildPartner(
                position: 1, cardId: 30001, friendship: 80, commandId: 102,
                scenarioId: 5,
                venusActiveSpirits: [new SingleModeVenusActiveSpiritEffect { chara_id = 9042, effect_group_id = 421 }]);

            Assert.True(p.Shining);
            Assert.Equal(PartnerPriority.闪, p.Priority); // 非友人 → 闪
        }

        [Fact]
        public void Shining_False_WhenGrandMastersSpiritEffectMissing()
        {
            // GM 场景但激活精灵不满足(effect_group_id 不是 421)，且常规得意判定也不命中 → 不闪。
            var p = BuildPartner(
                position: 1, cardId: 30001, friendship: 80, commandId: 102,
                scenarioId: 5,
                venusActiveSpirits: [new SingleModeVenusActiveSpiritEffect { chara_id = 9042, effect_group_id = 999 }]);

            Assert.False(p.Shining);
        }

        [Fact]
        public void Shining_True_ViaTeamCardLinkage_皇团()
        {
            // 团队卡联动：皇团(30067)且 chara_effect_id_array 含 101 → 强制 Shining。
            // 皇团 seed 成速卡但放在力训练(command_id=102)上，常规得意判定 false，仅靠团队卡分支命中。
            var p = BuildPartner(
                position: 3, cardId: 30067, friendship: 80, commandId: 102,
                charaEffectIds: [101]);

            Assert.True(p.Shining);
            Assert.Equal(PartnerPriority.闪, p.Priority); // 30067 seed 为非友人 → 闪
        }

        [Fact]
        public void Shining_True_ViaTeamCardLinkage_神团FriendKeepsFriendPriority()
        {
            // 神团(30137)且 chara_effect_id_array 含 102 → 强制 Shining。
            // 神团 seed 为友人卡([友])：Shining 块里命中 Name.Contains("[友]") → Priority 仍是友人。
            var p = BuildPartner(
                position: 4, cardId: 30137, friendship: 80, commandId: 102,
                charaEffectIds: [102]);

            Assert.True(p.Shining);
            Assert.Equal(PartnerPriority.友人, p.Priority);
        }

        [Fact]
        public void Shining_False_WhenTeamCardEffectIdMissing()
        {
            // 皇团但 chara_effect_id_array 不含 101 → 团队卡分支不触发；常规得意也不命中 → 不闪。
            var p = BuildPartner(
                position: 3, cardId: 30067, friendship: 80, commandId: 102,
                charaEffectIds: [999]);

            Assert.False(p.Shining);
        }

        // ---- TrainingPartner.IsNpc / Priority(NPC) ---------------------------------------------

        [Fact]
        public void IsNpc_True_AndKeyNpcPriority_ForChairman()
        {
            // Position=101 不在 1..6 → IsNpc=true；且落在 100..1000 → 关键NPC。
            // 走 GetCharacter(101) 取本名，无需 support_card_array。
            var p = BuildPartner(position: 101, cardId: 0, friendship: 50, commandId: 101);

            Assert.True(p.IsNpc);
            Assert.Equal(PartnerPriority.关键NPC, p.Priority);
            Assert.False(p.Shining); // NPC 分支永不置 Shining
        }

        [Fact]
        public void IsNpc_True_AndDefaultPriority_ForGuestBeyond1000()
        {
            // Position=1001 (>1000) → IsNpc=true；非 100..1000、非凯旋门同伴(场景非 LArc) → Priority 保持默认。
            var p = BuildPartner(position: 1001, cardId: 0, friendship: 50, commandId: 101);

            Assert.True(p.IsNpc);
            Assert.Equal(PartnerPriority.默认, p.Priority);
            Assert.False(p.IsArcPartner); // 非 LArc 场景
        }

        [Theory]
        [InlineData(1, false)]  // 1..6 都是自带 S 卡，非 NPC
        [InlineData(6, false)]
        [InlineData(7, true)]   // 7 在 1..6 之外 → NPC
        [InlineData(0, true)]   // 0 也在区间外 → NPC
        public void IsNpc_BoundaryByPosition(int position, bool expectedIsNpc)
        {
            // 仅验证 IsNpc 的 Position 边界(1..6 为自带卡)。position∈1..6 时需要 support_card_array 提供卡。
            var cardId = position is >= 1 and <= 6 ? 30001 : 0;
            var p = BuildPartner(position: position, cardId: cardId, friendship: 50, commandId: 101);

            Assert.Equal(expectedIsNpc, p.IsNpc);
        }

        // ---- CommandInfo.TrainIndex --------------------------------------------------------------

        [Theory]
        [InlineData(1101, 1)] // ToTrainIndex[1101]=0 → TrainIndex=0+1=1
        [InlineData(1105, 5)] // =4 → 5
        [InlineData(605, 5)]  // =4 → 5
        [InlineData(101, 1)]  // =0 → 1
        [InlineData(906, 5)]  // =4 → 5
        public void TrainIndex_MapsCommandIdToOneBasedSlot(int commandId, int expected)
        {
            var info = MakeCommandInfo(commandId);
            Assert.Equal(expected, info.TrainIndex);
        }

        [Fact]
        public void TrainIndex_ZeroWhenCommandIdUnknown()
        {
            // 9999 不在 ToTrainIndex 里 → TryGetValue 失败 → TrainIndex 保持默认 0
            var info = MakeCommandInfo(9999);
            Assert.Equal(0, info.TrainIndex);
        }

        [Fact]
        public void TrainIndex_HonorsCustomDictionary()
        {
            // 传入自定义映射应覆盖内置 FrozenDictionary（commandId=777→映射值5→TrainIndex=6）
            var resp = MakeResp([], [], [Command(777, [])]);
            var turn = new TurnInfo(resp);
            var info = new CommandInfo(resp, turn, 777, trainIndexDictionary: new Dictionary<int, int> { [777] = 5 });

            Assert.Equal(6, info.TrainIndex);
        }

        // ---- CommandInfo.TrainLevel --------------------------------------------------------------

        [Fact]
        public void TrainLevel_ReadsFromTrainingLevelInfoArray()
        {
            // training_level_info_array 含 {command_id=1101, level=3} → TrainLevel=3
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
            // 数组里没有匹配的 command_id → FirstOrDefault 返回 null → TrainLevel=0
            var resp = MakeResp(
                [], [], [Command(1101, [])],
                trainingLevels: [new TrainingLevelInfo { command_id = 1102, level = 5 }]);
            var turn = new TurnInfo(resp);
            var info = new CommandInfo(resp, turn, 1101);

            Assert.Equal(0, info.TrainLevel);
        }

        // ---- CommandInfo.TrainingPartners --------------------------------------------------------

        [Fact]
        public void TrainingPartners_BuiltAndSortedByPriority()
        {
            // 同一个速训练(command_id=101)里放两张卡：
            //   位1=速卡羁绊80在速位 → 闪(Priority=1)
            //   位2=速卡羁绊50      → 羁绊不足(Priority=2)
            // OrderBy(Priority) 应把"闪"排在"羁绊不足"前。
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

        // ---- helper：构造一个只关心 TrainIndex 的 CommandInfo（command 无 partner）----------------

        static CommandInfo MakeCommandInfo(int commandId)
        {
            var resp = MakeResp([], [], [Command(commandId, [])]);
            var turn = new TurnInfo(resp);
            return new CommandInfo(resp, turn, commandId);
        }
    }
}
