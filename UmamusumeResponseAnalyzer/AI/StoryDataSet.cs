using Gallop;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.AI
{
    public class LArcRivalData
    {
        public int id;     // chara_id
        public int commandId;  // 电池上面的属性
        public int atTrain = -1; // 所在的位置（额外记录）
        public int boost;  // 电量
        public int chargeNum;   // 当前回合可以充多少
        public int lv;     // 电池等级 star_lv
        public int specialEffect; // 特殊电池Buff ID, 0为未知！
        public int[] nextThreeEffects; // 下三次Buff加成
        public bool hasAiJiao;  // 是否有爱娇Buff
        public bool hasTrain;   // 是否有擅长练习Buff

        public LArcRivalData() { }
        public LArcRivalData(SingleModeArcRival rival)
        {
            id = rival.chara_id;
            commandId = rival.command_id;
            boost = rival.rival_boost;
            lv = rival.star_lv;
            hasAiJiao = hasTrain = false;
            specialEffect = 0;
            nextThreeEffects = new int[3];
            if (rival.selection_peff_array != null)
            {
                for (int i = 0; i < 3; ++i)
                {
                    int effect = rival.selection_peff_array[i].effect_group_id;
                    nextThreeEffects[i] = effect;

                    if (effect == 8)
                        hasAiJiao = true;
                    else if (effect == 9)
                        hasTrain = true;
                    else if (effect != 1 && effect != 11)   // 不是属性和技能点
                        specialEffect = effect;
                }
            }
        }
    }

    // 从SingleModeArcDataSet导出的LArc剧本机制数值
    public class LArcDataSet
    {
        public double approvalRate;        // 支持度，换算成百分比
        public int[] lessonLevels;         // 课程等级（10项）
        public int shixingPt;                 // 适性PT
        public LArcRivalData[] rivals;     // 人头（电池Buff）数据
        public int ssApprovalRate;         // 当前ss训练的支持度加值
        public Dictionary<int, int> ssStatus;             // 当前ss训练的属性
        public Dictionary<int, int> ssBonusStatus;        // 当前ss训练的蓝字（上层）属性
        public bool isSpecialMatch;        // 是否为SSS
        // 训练数据
        public int[] shiningCount;         // 各个训练的彩圈数
        public bool[] friendAppear;         // 友人位置
        public int[] chargedNum;            // 充电格数
        public int[] chargedFullNum;        // 充满格数
        public int turn;
        public bool isAbroad;              // 是否在海外
        public bool isBegin;                // 是否刚开始(1-2回合)

        public LArcDataSet(Gallop.SingleModeCheckEventResponse @event)
        {
            SingleModeArcDataSet data = @event.data.arc_data_set;
            int turn = @event.data.chara_info.turn - 1; // 从0开始

            approvalRate = data.arc_info.approval_rate;
            shixingPt = data.arc_info.global_exp;
            ssApprovalRate = data.selection_info.all_win_approval_point;
            isSpecialMatch = Convert.ToBoolean(data.selection_info.is_special_match);
            this.turn = turn;
            isAbroad = (turn >= 36 && turn <= 42) || (turn >= 60 && turn <= 66);
            isBegin = (turn <= 1);

            if (!isBegin)
            {
                lessonLevels = new int[10];
                ssStatus = new Dictionary<int, int>()
                {
                    {1,0},
                    {2,0},
                    {3,0},
                    {4,0},
                    {5,0},
                    {30,0},
                    {10,0},
                };
                ssBonusStatus = new Dictionary<int, int>()
                {
                    {1,0},
                    {2,0},
                    {3,0},
                    {4,0},
                    {5,0},
                    {30,0},
                    {10,0},
                };
                rivals = new LArcRivalData[data.arc_rival_array.Length];

                // 课程信息
                foreach (var item in data.arc_info.potential_array)
                    lessonLevels[item.potential_id - 1] = item.level;
                // 人头信息
                for (int i = 0; i < data.arc_rival_array.Length; ++i)
                {
                    rivals[i] = new LArcRivalData(data.arc_rival_array[i]);
                }
                // SS训练属性值
                foreach (var item in data.selection_info.params_inc_dec_info_array)
                    ssStatus[item.target_type] += item.value;
                foreach (var item in data.selection_info.bonus_params_inc_dec_info_array)
                    ssBonusStatus[item.target_type] += item.value;

                shiningCount = new int[5] { 0, 0, 0, 0, 0 };
                friendAppear = new bool[5] { false, false, false, false, false };
                chargedNum = new int[5] { 0, 0, 0, 0, 0 };
                chargedFullNum = new int[5] { 0, 0, 0, 0, 0 };

                // 分析彩圈，充电数和友人位置
                var supportCards = @event.data.chara_info.support_card_array.ToDictionary(x => x.position, x => x.support_card_id); //当前S卡卡组
                var commandInfo = new Dictionary<int, string[]>();
                foreach (var command in @event.data.home_info.command_info_array)
                {
                    if (!GameGlobal.ToTrainIndex.ContainsKey(command.command_id)) continue;
                    var trainIdx = GameGlobal.ToTrainIndex[command.command_id];
                    // 计算当前训练的彩圈数量
                    foreach (var partner in command.training_partner_array)
                    {
                        var name = Database.SupportIdToShortName[(partner >= 1 && partner <= 7) ? supportCards[partner] : partner].EscapeMarkup(); //partner是当前S卡卡组的index（1~6，7是啥？我忘了）或者charaId（10xx)
                        var friendship = @event.data.chara_info.evaluation_info_array.First(x => x.target_id == partner).evaluation;
                        bool shouldShining = false; //是不是彩圈
                        // 是携带的卡，判断是否有彩圈
                        if (partner >= 1 && partner <= 7)
                        {
                            // 判断是否为友人
                            if (supportCards[partner] == 30160 || supportCards[partner] == 10094) //佐岳友人卡
                                friendAppear[trainIdx] = true;
                            // 普通卡：是否在得意位置上
                            var commandId1 = GameGlobal.ToTrainId[command.command_id];
                            shouldShining = friendship >= 80 &&
                                name.Contains(commandId1 switch
                                {
                                    101 => "[速]",
                                    105 => "[耐]",
                                    102 => "[力]",
                                    103 => "[根]",
                                    106 => "[智]",
                                });

                            // 团队卡
                            if ((supportCards[partner] == 30137 && @event.data.chara_info.chara_effect_id_array.Any(x => x == 102)) || //神团
                            (supportCards[partner] == 30067 && @event.data.chara_info.chara_effect_id_array.Any(x => x == 101)) || //皇团
                            (supportCards[partner] == 30081 && @event.data.chara_info.chara_effect_id_array.Any(x => x == 100)) //天狼星
                            )
                            {
                                shouldShining = true;
                            }
                            if (shouldShining)
                            {
                                shiningCount[trainIdx] += 1;
                            }
                        }   // if partner
                    }   // foreach partner

                    int maxCharge = 1 + shiningCount[trainIdx];

                    // 根据彩圈数量，计算充电数量
                    foreach (var partner in command.training_partner_array)
                    {
                        bool isArcPartner = @event.IsScenario(ScenarioType.LArc) && (partner > 1000 || (partner >= 1 && partner <= 7)) && @event.data.arc_data_set.evaluation_info_array.Any(x => x.target_id == partner);
                        // 如果有电池槽，则记录位置及判断充电格数
                        if (isArcPartner)
                        {
                            // 从卡号到角色名
                            var chara_id = @event.data.arc_data_set.evaluation_info_array.First(x => x.target_id == partner).chara_id;
                            if (rivals.Any(x => x.id == chara_id))
                            {
                                var rv = rivals.First(x => x.id == chara_id);
                                rv.atTrain = GameGlobal.ToTrainIndex[command.command_id];    // 记录该NPC所在位置
                                var charge = Math.Min(maxCharge, 3 - rv.boost);
                                if (!isAbroad)
                                {
                                    rv.chargeNum = charge;
                                    chargedNum[trainIdx] += charge;
                                    if (rv.boost + charge >= 3)
                                        ++chargedFullNum[trainIdx];
                                }

                            }
                        }
                    }
                }   // foreach command
            } // if not isBegin
        } // ctor
    } // class

    public class VenusDataSet
    {
        public int venusLevelYellow;
        public int venusLevelRed;
        public int venusLevelBlue;

        public int[] venusSpiritsBottom;
        public int[] venusSpiritsUpper;
        public int venusAvailableWisdom;
        public bool venusIsWisdomActive;

        //神团卡专属
        public bool venusCardFirstClick;// 是否已经点击过神团卡
        public bool venusCardUnlockOutgoing;// 是否解锁外出
        public bool venusCardIsQingRe;// 情热zone
        public int venusCardQingReContinuousTurns;//女神连着情热了几个回合
        public bool[] venusCardOutgoingUsed;// 用过哪些出行，依次是红黄蓝和最后两个
        
        public int[] spiritBonus;//碎片加成
        public int[] spiritDistribution;//碎片分布，依次是五训练01234，休息5，外出6，比赛7。若为2碎片，则加32

        public VenusDataSet(Gallop.SingleModeCheckEventResponse @event)
        {
            venusCardOutgoingUsed = new bool[] { false, false, false, false, false };
            venusCardUnlockOutgoing = false;
            venusCardOutgoingUsed = new bool[5] { false, false, false, false, false };
            venusCardIsQingRe = @event.data.chara_info.chara_effect_id_array.Contains(102);
            venusCardQingReContinuousTurns = 0;
            venusCardFirstClick = false;
            spiritDistribution = new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 };

            // 额外信息
            int turn = @event.data.chara_info.turn - 1;
            int[] cardId = new int[6];
            foreach (var s in @event.data.chara_info.support_card_array)
            {
                int p = s.position - 1;
                //100000*突破数+卡原来的id，例如神团是30137，满破神团就是430137
                cardId[p] = s.limit_break_count * 100000 + s.support_card_id;
            }

            if (venusCardIsQingRe)
            {
                int continuousTurnNum = 1;
                for (int i = turn; i >= 1; i--)
                {
                    if (GameStats.stats[i] == null || !GameStats.stats[i].venus_isEffect102)
                        break;
                    continuousTurnNum++;
                }
                venusCardQingReContinuousTurns = continuousTurnNum;
                //AnsiConsole.MarkupLine($"女神彩圈已持续[green]{continuousTurnNum}[/]回合");
            }
            for (int t = 1; t <= turn; t++)//不考虑当前回合，因为当前回合还未选训练
            {
                var turnStat = GameStats.stats[t];
                if (turnStat == null) continue;//有可能这局中途才打开小黑板
                if (turnStat.isTraining && turnStat.venus_venusTrain == turnStat.playerChoice)
                {
                    venusCardFirstClick = true;
                    break;
                }
            }
            if (venusCardUnlockOutgoing)
                venusCardFirstClick = true;//半途重启小黑板

            foreach (var i in @event.data.venus_data_set.venus_chara_command_info_array)
            {
                int p = -1;
                if (i.command_type == 1)
                    p = GameGlobal.ToTrainIndex[i.command_id];
                else if (i.command_type == 3)
                    p = 6;
                else if (i.command_type == 4)
                    p = 7;
                else if (i.command_type == 7)
                    p = 5;
                int s = i.spirit_id;
                if (i.is_boost > 0)
                    s += 32;
                if (p != -1)
                    spiritDistribution[p] = s;

            }
            venusLevelRed = @event.data.venus_data_set.venus_chara_info_array.Any(x => x.chara_id == 9040) ?
                @event.data.venus_data_set.venus_chara_info_array.First(x => x.chara_id == 9040).venus_level : 0;
            venusLevelBlue = @event.data.venus_data_set.venus_chara_info_array.Any(x => x.chara_id == 9041) ?
                @event.data.venus_data_set.venus_chara_info_array.First(x => x.chara_id == 9041).venus_level : 0;
            venusLevelYellow = @event.data.venus_data_set.venus_chara_info_array.Any(x => x.chara_id == 9042) ?
                @event.data.venus_data_set.venus_chara_info_array.First(x => x.chara_id == 9042).venus_level : 0;

            venusSpiritsBottom = new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 };
            venusSpiritsUpper = new int[6] { 0, 0, 0, 0, 0, 0 };
            venusAvailableWisdom = 0;
            venusIsWisdomActive = false;

            for (int spiritPlace = 1; spiritPlace <= 15; spiritPlace++)
            {
                int spiritId =
                    @event.data.venus_data_set.spirit_info_array.Any(x => x.spirit_num == spiritPlace)
                    ? @event.data.venus_data_set.spirit_info_array.First(x => x.spirit_num == spiritPlace).spirit_id
                    : 0;

                if (spiritPlace < 9)
                    venusSpiritsBottom[spiritPlace - 1] = spiritId;
                else if (spiritPlace < 15)
                    venusSpiritsUpper[spiritPlace - 9] = spiritId;
                else
                {
                    if (spiritId == 9040)
                        venusAvailableWisdom = 1;
                    else if (spiritId == 9041)
                        venusAvailableWisdom = 2;
                    else if (spiritId == 9042)
                        venusAvailableWisdom = 3;
                }
            }
            if (@event.data.venus_data_set.venus_spirit_active_effect_info_array.Any(x => x.chara_id >= 9040))
                venusIsWisdomActive = true;
            foreach (var s in @event.data.chara_info.evaluation_info_array)
            {
                int p = s.target_id - 1;
                if (p < 6 && cardId[p] % 100000 == 30137) // 女神
                {
                    venusCardUnlockOutgoing = s.is_outing > 0;
                    if (venusCardUnlockOutgoing)
                    {
                        venusCardOutgoingUsed[0] = s.group_outing_info_array.First(x => x.chara_id == 9040).story_step > 0;
                        venusCardOutgoingUsed[1] = s.group_outing_info_array.First(x => x.chara_id == 9041).story_step > 0;
                        venusCardOutgoingUsed[2] = s.group_outing_info_array.First(x => x.chara_id == 9042).story_step > 0;
                        venusCardOutgoingUsed[3] = s.story_step > 0;
                        venusCardOutgoingUsed[4] = s.story_step > 1;
                    }
                }
            }
        } // ctor
    } // class VenusDataSet
}
