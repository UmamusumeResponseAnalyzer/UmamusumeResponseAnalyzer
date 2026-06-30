using Gallop;
using Gallop.Mecha;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// 用<b>合成输入</b>(手搓 Gallop DTO，不依赖抓包语料)精确钉死 <see cref="TurnInfo"/> 及各场景子类的
    /// 计算属性逻辑。期望值全部按生产代码推算，默认信生产代码。
    ///
    /// 关键难点(为什么这些工厂这样搭)：
    /// - 子类构造里普遍会对 <c>data_set.command_info_array</c> 逐项 new <see cref="UmamusumeResponseAnalyzer.Game.CommandInfo"/>。
    ///   CommandInfo 构造体内 <c>home_info.command_info_array.First(命中 command_id)</c> 不命中即抛——
    ///   故任何会被构造出 CommandInfo 的 command_id，都必须在 home_info 里有对应条目(用 <see cref="HomeCommand"/> 造)。
    /// - <c>training_partner_array</c> 一旦非空，CommandInfo 会进而 new TrainingPartner，后者要查 <c>Database.Names</c>。
    ///   本测试把 partner 数组留空 → 不触碰 Database → 整个类无需 [Collection("Database")] / seed。
    ///   (注意:即便如此，CommandInfo 构造仍会读 <c>chara_info.training_level_info_array</c>(FirstOrDefault，空数组安全)
    ///    与 home_info 条目，故下面 chara/home 的所有数组都必须非 null。)
    /// </summary>
    public class TurnInfoTests
    {
        // ---------- 合成输入工厂 ----------

        /// <summary>造一个五维/上限/回合/卡号可控、所有数组非 null 的 chara_info。</summary>
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
            };

        /// <summary>home_info 里的一条普通命令：partner 数组留空，从而不会牵出 TrainingPartner→Database。</summary>
        static SingleModeCommandInfo HomeCommand(int commandId) => new()
        {
            command_id = commandId,
            training_partner_array = [],
            tips_event_partner_array = [],
        };

        /// <summary>包出 CommonResponse；homeCommandIds 决定 home_info 里有哪些命令(供 CommandInfo 命中)。</summary>
        static SingleModeCheckEventResponse.CommonResponse MakeResp(
            SingleModeChara chara, params int[] homeCommandIds) => new()
            {
                chara_info = chara,
                home_info = new SingleModeHomeInfo
                {
                    command_info_array = [.. homeCommandIds.Select(HomeCommand)],
                    free_continue_time = 0,
                },
                unchecked_event_array = [],
            };

        // =====================================================================
        //  基类 TurnInfo
        // =====================================================================

        // ReviseOver1200: x>1200 ? x*2-1200 : x —— 任务点名的三个边界
        [Theory]
        [InlineData(1200, 1200)]   // 等于阈值，不放大
        [InlineData(1201, 1202)]   // 刚过阈值：1201*2-1200
        [InlineData(1300, 1400)]   // 1300*2-1200
        [InlineData(1000, 1000)]   // 阈值以下原样
        [InlineData(0, 0)]
        public void ReviseOver1200_AppliedToSpeedRevised(int raw, int expected)
        {
            var turn = new TurnInfo(MakeResp(MakeChara(speed: raw)));
            Assert.Equal(expected, turn.SpeedRevised);
        }

        [Fact]
        public void StatsRevised_AppliesFormulaPerStat()
        {
            // 速=1300→1400, 耐=1200→1200, 力=1201→1202, 根=600→600, 智=2000→2800
            var turn = new TurnInfo(MakeResp(
                MakeChara(speed: 1300, stamina: 1200, power: 1201, guts: 600, wiz: 2000)));
            Assert.Equal([1400, 1200, 1202, 600, 2800], turn.StatsRevised);
        }

        [Fact]
        public void MaxStatsRevised_AppliesFormulaPerStat()
        {
            // max 同样走 ReviseOver1200：1250→1300, 1200→1200, 其余 <1200 原样
            var turn = new TurnInfo(MakeResp(MakeChara(
                maxSpeed: 1250, maxStamina: 1200, maxPower: 800, maxGuts: 1300, maxWiz: 1100)));
            Assert.Equal([1300, 1200, 800, 1400, 1100], turn.MaxStatsRevised);
        }

        [Fact]
        public void CharacterId_TakesFirstFourDigitsOfCardId()
        {
            // card_id=100502 → "100502"[..4] => "1005" => 1005
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
        public void TotalStats_IsSumOfStatsRevised()
        {
            // 全部 <1200，TotalStats == 原始五维之和；同时验证用的是 Revised 而非 Stats
            var turn = new TurnInfo(MakeResp(
                MakeChara(speed: 100, stamina: 200, power: 300, guts: 400, wiz: 500)));
            Assert.Equal(1500, turn.TotalStats);
        }

        [Fact]
        public void TotalStats_UsesRevisedValues_NotRaw()
        {
            // 速=1300(→1400) 其余 0 → Revised 和=1400，证明求和发生在放大之后
            var turn = new TurnInfo(MakeResp(
                MakeChara(speed: 1300, stamina: 0, power: 0, guts: 0, wiz: 0)));
            Assert.Equal(1400, turn.TotalStats);
        }

        // Year=(Turn-1)/24+1; Month=((Turn-1)%24)/2+1; HalfMonth: turn 偶=后半, 奇=前半
        [Theory]
        [InlineData(1, 1, 1, "前半")]    // 第1回合：1年1月前半
        [InlineData(2, 1, 1, "后半")]    // 偶数→后半
        [InlineData(24, 1, 12, "后半")]  // 第1年最后一回合
        [InlineData(25, 2, 1, "前半")]   // 跨入第2年
        [InlineData(48, 2, 12, "后半")]
        [InlineData(49, 3, 1, "前半")]
        public void Year_Month_HalfMonth_DerivedFromTurn(int turn, int year, int month, string half)
        {
            var t = new TurnInfo(MakeResp(MakeChara(turn: turn)));
            Assert.Equal(year, t.Year);
            Assert.Equal(month, t.Month);
            Assert.Equal(half, t.HalfMonth);
        }

        [Fact]
        public void Turn_VitalAndMaxVital_PassThrough()
        {
            var t = new TurnInfo(MakeResp(MakeChara(turn: 37, vital: 65, maxVital: 120)));
            Assert.Equal(37, t.Turn);
            Assert.Equal(65, t.Vital);
            Assert.Equal(120, t.MaxVital);
        }

        [Theory]
        [InlineData(1, ScenarioType.Ura)]
        [InlineData(6, ScenarioType.LArc)]
        [InlineData(8, ScenarioType.Cook)]
        [InlineData(13, ScenarioType.Breeders)]
        public void Scenario_CastFromScenarioId(int scenarioId, ScenarioType expected)
        {
            var t = new TurnInfo(MakeResp(MakeChara(scenarioId: scenarioId)));
            Assert.Equal(expected, t.Scenario);
        }

        // =====================================================================
        //  TurnInfoArc —— 不构造 CommandInfo，纯拷贝 + 两个计算属性
        // =====================================================================

        static SingleModeCheckEventResponse.CommonResponse MakeArcResp(int turn, int approvalRate)
        {
            var resp = MakeResp(MakeChara(scenarioId: (int)ScenarioType.LArc, turn: turn));
            resp.arc_data_set = new SingleModeArcDataSet
            {
                arc_info = new SingleModeArcInfo { approval_rate = approvalRate },
                arc_rival_array = [],
                rival_race_info_array = [],
                selection_info = new SingleModeArcSelectionInfo(),
                command_info_array = [],
                race_history_array = [],
                evaluation_info_array = [],
            };
            return resp;
        }

        [Fact]
        public void Arc_ApprovalRate_ReadsArcInfo()
        {
            var arc = new TurnInfoArc(MakeArcResp(turn: 1, approvalRate: 77));
            Assert.Equal(77, arc.ApprovalRate);
        }

        // IsAbroad: (37<=Turn<=43) || (61<=Turn<=67)
        [Theory]
        [InlineData(36, false)] // 区间外(下边界前一格)
        [InlineData(37, true)]  // 第一段下边界
        [InlineData(43, true)]  // 第一段上边界
        [InlineData(44, false)] // 两段之间
        [InlineData(60, false)] // 第二段下边界前一格
        [InlineData(61, true)]  // 第二段下边界
        [InlineData(67, true)]  // 第二段上边界
        public void Arc_IsAbroad_TwoTurnWindows(int turn, bool expected)
        {
            var arc = new TurnInfoArc(MakeArcResp(turn: turn, approvalRate: 0));
            Assert.Equal(expected, arc.IsAbroad);
        }

        [Fact]
        public void Arc_TotalTurns_ShadowsBaseWith67()
        {
            // 子类用 new int TotalTurns = 67 遮蔽基类的 78；经静态类型 TurnInfoArc 访问取到 67
            var arc = new TurnInfoArc(MakeArcResp(turn: 1, approvalRate: 0));
            Assert.Equal(67, arc.TotalTurns);
        }

        // =====================================================================
        //  TurnInfoCook —— Select 出 CommandInfo 后 Where(TrainIndex != 0)
        // =====================================================================

        [Fact]
        public void Cook_CommandInfoArray_FiltersOutTrainIndexZero()
        {
            // 101→TrainIndex 1(速)，105→2(耐) 会留下;1(非训练命令，不在 ToTrainIndex)→TrainIndex 0 被滤掉。
            var chara = MakeChara(scenarioId: (int)ScenarioType.Cook);
            var resp = MakeResp(chara, 101, 105, 1); // home 必须含这三个 command_id
            resp.cook_data_set = new SingleModeCookDataSet
            {
                command_info_array =
                [
                    new SingleModeCookCommandInfo { command_id = 101 },
                    new SingleModeCookCommandInfo { command_id = 105 },
                    new SingleModeCookCommandInfo { command_id = 1 },
                ],
                material_harvest_info_array = [],
                care_history_info_array = [],
                material_info_array = [],
                facility_info_array = [],
                command_material_care_info_array = [],
            };

            var cook = new TurnInfoCook(resp);
            var ids = cook.CommandInfoArray.Select(x => x.CommandId).OrderBy(x => x).ToArray();
            Assert.Equal([101, 105], ids);
            Assert.All(cook.CommandInfoArray, c => Assert.NotEqual(0, c.TrainIndex));
        }

        // =====================================================================
        //  TurnInfoMecha —— MechaCommandInfo 额外解析 point_up/is_recommend/energy_num
        // =====================================================================

        [Fact]
        public void Mecha_CommandInfoArray_FiltersZero_AndMapsMechaFields()
        {
            var chara = MakeChara(scenarioId: (int)ScenarioType.Mecha);
            var resp = MakeResp(chara, 102, 1);
            resp.mecha_data_set = new SingleModeMechaDataSet
            {
                command_info_array =
                [
                    new SingleModeMechaCommandInfo
                    {
                        command_id = 102, // →TrainIndex 3(力)，保留
                        is_recommend = true,
                        energy_num = 5,
                        point_up_info_array =
                        [
                            new SingleModeMechaPointUpInfo { status_type = 2, value = 30 },
                        ],
                    },
                    new SingleModeMechaCommandInfo
                    {
                        command_id = 1,   // TrainIndex 0，被滤
                        point_up_info_array = [],
                    },
                ],
            };

            var mecha = new TurnInfoMecha(resp);
            var only = Assert.Single(mecha.CommandInfoArray);
            Assert.Equal(102, only.CommandId);
            Assert.True(only.IsRecommend);
            Assert.Equal(5, only.EnergyNum);
            Assert.Equal((2, 30), Assert.Single(only.PointUpInfoArray));
        }

        // =====================================================================
        //  TurnInfoLegend —— 三个聚合：CommandGauges / GaugeCountDictonary / CommandInfoArray
        // =====================================================================

        [Fact]
        public void Legend_Gauges_AndCommandFiltering()
        {
            var chara = MakeChara(scenarioId: (int)ScenarioType.Legend);
            // CommandInfoArray 只构造 command_id ∉ {0,701,401,801} 的；故 home 仅需含 101、102。
            var resp = MakeResp(chara, 101, 102);
            resp.legend_data_set = new SingleModeLegendDataSet
            {
                command_info_array =
                [
                    new SingleModeLegendCommandInfo { command_id = 101, legend_id = 9048, gain_gauge = 3 },
                    new SingleModeLegendCommandInfo { command_id = 102, legend_id = 9046, gain_gauge = 5 },
                    new SingleModeLegendCommandInfo { command_id = 701, legend_id = 9047, gain_gauge = 1 }, // 进 CommandGauges 但不进 CommandInfoArray
                ],
                gauge_count_array =
                [
                    new SingleModeLegendGauge { legend_id = 9048, count = 2 },
                    new SingleModeLegendGauge { legend_id = 9046, count = 4 },
                ],
            };

            var legend = new TurnInfoLegend(resp);

            // CommandGauges: command_id → (legend_id, gain_gauge)，三条全收
            Assert.Equal((9048, 3), legend.CommandGauges[101]);
            Assert.Equal((9046, 5), legend.CommandGauges[102]);
            Assert.Equal((9047, 1), legend.CommandGauges[701]);

            // GaugeCountDictonary: legend_id → count
            Assert.Equal(2, legend.GaugeCountDictonary[9048]);
            Assert.Equal(4, legend.GaugeCountDictonary[9046]);

            // CommandInfoArray 排除了 701
            var ids = legend.CommandInfoArray.Select(x => x.CommandId).OrderBy(x => x).ToArray();
            Assert.Equal([101, 102], ids);
        }

        // =====================================================================
        //  TurnInfoPioneer —— 对每条命令都 new CommandInfo(连 3101 也构造)，仅 3101 不入列表
        // =====================================================================

        [Fact]
        public void Pioneer_ExcludesCommand3101FromList_ButStillNeedsItInHome()
        {
            var chara = MakeChara(scenarioId: (int)ScenarioType.Pioneer);
            // 即便 3101 不入 CommandInfoArray，构造时也会 new CommandInfo(3101) → home 必须含 3101。
            var resp = MakeResp(chara, 101, 3601, 3101);
            resp.pioneer_data_set = new SingleModePioneerDataSet
            {
                command_info_array =
                [
                    new SingleModePioneerCommandInfo { command_id = 101 },
                    new SingleModePioneerCommandInfo { command_id = 3601 }, // 夏合宿映射，入列表
                    new SingleModePioneerCommandInfo { command_id = 3101 }, // 被排除
                ],
                pioneer_point_gain_info_array =
                [
                    new SingleModePioneerPointGainInfo { command_id = 101, gain_num = 12 },
                    new SingleModePioneerPointGainInfo { command_id = 3601, gain_num = 8 },
                ],
            };

            var pioneer = new TurnInfoPioneer(resp);
            var ids = pioneer.CommandInfoArray.Select(x => x.CommandId).OrderBy(x => x).ToArray();
            Assert.Equal([101, 3601], ids);

            // PointGainInfoDictionary: command_id → gain_num
            Assert.Equal(12, pioneer.PointGainInfoDictionary[101]);
            Assert.Equal(8, pioneer.PointGainInfoDictionary[3601]);

            // 3601 经 Pioneer.ToTrainIndex[3601]=0 → CommandInfo.TrainIndex=1
            var c3601 = pioneer.CommandInfoArray.First(x => x.CommandId == 3601);
            Assert.Equal(1, c3601.TrainIndex);
        }

        // =====================================================================
        //  TurnInfoOnsen —— 仅 command_type==1 的命令进列表(且只为它们构造 CommandInfo)
        // =====================================================================

        [Fact]
        public void Onsen_OnlyCommandType1_AreConstructed()
        {
            var chara = MakeChara(scenarioId: (int)ScenarioType.Onsen);
            // command_type==1 的是 101、601；type!=1 的 102 不构造 → home 无需含 102。
            var resp = MakeResp(chara, 101, 601);
            resp.onsen_data_set = new SingleModeOnsenDataSet
            {
                command_info_array =
                [
                    new SingleModeOnsenCommandInfo { command_id = 101, command_type = 1 },
                    new SingleModeOnsenCommandInfo { command_id = 601, command_type = 1 },
                    new SingleModeOnsenCommandInfo { command_id = 102, command_type = 9 }, // 被 Where 滤，不构造
                ],
            };

            var onsen = new TurnInfoOnsen(resp);
            var ids = onsen.CommandInfoArray.Select(x => x.CommandId).OrderBy(x => x).ToArray();
            Assert.Equal([101, 601], ids);
        }

        // =====================================================================
        //  TurnInfoBreeders —— command_type==1 过滤 + team/sp 聚合
        // =====================================================================

        [Fact]
        public void Breeders_FiltersType1_AndAggregatesTeamAndSpInfo()
        {
            var chara = MakeChara(scenarioId: (int)ScenarioType.Breeders);
            var resp = MakeResp(chara, 105, 601);
            resp.breeders_data_set = new SingleModeBreedersDataSet
            {
                command_info_array =
                [
                    new SingleModeBreedersCommandInfo
                    {
                        command_id = 105, command_type = 1,
                        team_member_info_array = [ new SingleModeBreedersCommandTeamMemberInfo { chara_id = 1004, gain_exp = 50 } ],
                    },
                    new SingleModeBreedersCommandInfo
                    {
                        command_id = 601, command_type = 1,
                        team_member_info_array = [],
                    },
                    new SingleModeBreedersCommandInfo
                    {
                        command_id = 102, command_type = 7, // 非训练，不构造 CommandInfo
                        team_member_info_array = [],
                    },
                ],
                team_member_info_array =
                [
                    new SingleModeBreedersTeamMemberInfo { chara_id = 1004, rank = 7, exp = 100 },
                ],
                team_sp_training_info = new SingleModeBreedersTeamSpTrainingInfo
                {
                    stock_num = 2, stock_max = 5, activated_state = 1,
                },
            };

            var breeders = new TurnInfoBreeders(resp);

            // 仅 type==1 的两条入 CommandInfoArray
            var ids = breeders.CommandInfoArray.Select(x => x.CommandId).OrderBy(x => x).ToArray();
            Assert.Equal([105, 601], ids);

            // CommandTeamMemberInfoDictionary 用<全部>命令(含 type!=1)的 command_id 建键
            Assert.Equal(3, breeders.CommandTeamMemberInfoDictionary.Count);
            Assert.Equal(50, Assert.Single(breeders.CommandTeamMemberInfoDictionary[105]).gain_exp);

            // TeamMemberInfoDictionary: chara_id → 成员
            Assert.Equal(7, breeders.TeamMemberInfoDictionary[1004].rank);

            // 特训库存:activated_state==1 → Activated=true
            Assert.Equal(2, breeders.SpecialTrainingStock);
            Assert.Equal(5, breeders.SpecialTrainingMax);
            Assert.True(breeders.SpecialTrainingActivated);
        }

        [Fact]
        public void Breeders_NullCommandArray_StillReadsSpAndTeamInfo()
        {
            // command_info_array==null 时(生产代码有 null check)：跳过命令聚合，但 sp/team 仍读取。
            var chara = MakeChara(scenarioId: (int)ScenarioType.Breeders);
            var resp = MakeResp(chara); // home 无命令亦可，因不会构造任何 CommandInfo
            resp.breeders_data_set = new SingleModeBreedersDataSet
            {
                command_info_array = null!, // 故意置 null:覆盖生产代码的 null check 分支
                team_member_info_array = [],
                team_sp_training_info = new SingleModeBreedersTeamSpTrainingInfo
                {
                    stock_num = 0, stock_max = 3, activated_state = 0,
                },
            };

            var breeders = new TurnInfoBreeders(resp);
            Assert.Empty(breeders.CommandInfoArray);
            Assert.Empty(breeders.CommandTeamMemberInfoDictionary);
            Assert.Equal(3, breeders.SpecialTrainingMax);
            Assert.False(breeders.SpecialTrainingActivated); // activated_state 0 → false
        }
    }
}
