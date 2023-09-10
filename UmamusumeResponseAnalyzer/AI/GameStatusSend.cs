using Gallop;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Game;
using Newtonsoft.Json;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer.AI
{

    public class LArcRivalData
    {
        public int id;     // chara_id
        public int commandId;  // 对应的属性？
        public int boost;  // 电量
        public int lv;     // 电池等级 star_lv
        public int specialEffect; // 特殊电池Buff ID, 0为未知！
        public int[] nextThreeEffects; // 下三次Buff加成
        public bool hasAiJiao;  // 是否有爱娇Buff
        public bool hasTrain;   // 是否有擅长练习Buff
        public bool hasZhuMu;   // 是否有注目株Buff

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
            int turn = @event.data.chara_info.turn;

            approvalRate = data.arc_info.approval_rate;
            shixingPt = data.arc_info.global_exp;
            ssApprovalRate = data.selection_info.all_win_approval_point;
            isSpecialMatch = Convert.ToBoolean(data.selection_info.is_special_match);
            this.turn = turn;
            isAbroad = (turn >= 37 && turn <= 43) || (turn >= 61 && turn <= 67);
            isBegin = (turn <= 2);

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
                        // 如果有电池槽，则判断充电格数
                        if (isArcPartner && !isAbroad)
                        {
                            // 从卡号到角色名
                            var chara_id = @event.data.arc_data_set.evaluation_info_array.First(x => x.target_id == partner).chara_id;
                            if (rivals.Any(x => x.id == chara_id))
                            {
                                var rv = rivals.First(x => x.id == chara_id);
                                var charge = Math.Min(maxCharge, 3 - rv.boost);
                                chargedNum[trainIdx] += charge;
                                if (rv.boost + charge >= 3)
                                    ++chargedFullNum[trainIdx];
                            }
                        }
                    }
                }   // foreach command
            } // if not isBegin
        } // ctor
    } // class

    public class GameStatusSend
    {
        public int umaId;//马娘编号，见KnownUmas.cpp
        public int turn;//回合数，从0开始，到77结束
        public int vital;//体力，叫做“vital”是因为游戏里就这样叫的
        public int maxVital;//体力上限
        public bool isQieZhe;//切者
        public bool isAiJiao;//爱娇
        public int failureRateBias;//失败率改变量。练习上手=2，练习下手=-2
        public int[] fiveStatus;//五维属性，1200以上不减半
                                //int fiveStatusUmaBonus[5];//马娘自身加成
        public int[] fiveStatusLimit;//五维属性上限，1200以上不减半
        public int skillPt;//技能点
        public int motivation;//干劲，从1到5分别是绝不调到绝好调
        public int[] cardId;//6张卡的id
        public int[] cardJiBan;//羁绊，六张卡分别012345，理事长6，记者7
        public int[] trainLevelCount;//五个训练的等级的计数，实际训练等级=min(5,t/12+1)
        public int[] zhongMaBlueCount;//种马的蓝因子个数，假设只有3星
        public int[] zhongMaExtraBonus;//种马的剧本因子以及技能白因子（等效成pt），每次继承加多少。全大师杯因子典型值大约是30速30力200pt
        public bool isRacing;//这个回合是否在比赛


        //女神杯相关
        public int venusLevelYellow;//女神等级
        public int venusLevelRed;
        public int venusLevelBlue;

        public int[] venusSpiritsBottom;//底层碎片。8*颜色+属性。颜色012对应红蓝黄，属性123456对应速耐力根智pt。叫做“spirit”是因为游戏里就这样叫的
        public int[] venusSpiritsUpper;//按顺序分别是第二层和第三层的碎片，编号与底层碎片一致。*2还是*3现场算
        public int venusAvailableWisdom;//顶层的女神睿智，123分别是红蓝黄，0是没有
        public bool venusIsWisdomActive;//是否正在使用睿智

        // LArc
        public bool friendCardFirstClick;       // 是否点击过友人
        public bool friendCardUnlockOutgoing; // 友人卡是否出门
        public int friendCardOutgoingStage;   // 友人卡（假设只有一张）使用过的出行次数
        public LArcDataSet larcData;

        //神团卡专属
        public bool venusCardFirstClick;// 是否已经点击过神团卡
        public bool venusCardUnlockOutgoing;// 是否解锁外出
        public bool venusCardIsQingRe;// 情热zone
        public int venusCardQingReContinuousTurns;//女神连着情热了几个回合
        public bool[] venusCardOutgoingUsed;// 用过哪些出行，依次是红黄蓝和最后两个

        //当前回合的训练信息
        //0支援卡还未分配，1支援卡分配完毕或比赛开始前，2训练结束后或比赛结束后，0检查各种固定事件与随机事件并进入下一个回合
        //stageInTurn=0时可以输入神经网络输出估值，stageInTurn=1时可以输入神经网络输出policy
        public int stageInTurn;
        public bool[,] cardDistribution;//支援卡分布，六张卡分别012345，理事长6，记者7
        public bool[] cardHint;//六张卡分别有没有亮红点
        public int[] spiritDistribution;//碎片分布，依次是五训练01234，休息5，外出6，比赛7。若为2碎片，则加32

        //通过计算获得的信息
        public int[] spiritBonus;//碎片加成
        public int[,] trainValue;//第一个数是第几个训练，第二个数依次是速耐力根智pt体力
        public int[] failRate;//训练失败率
        
        public GameStatusSend(Gallop.SingleModeCheckEventResponse @event)
        {

            if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0) || @event.data.race_start_info != null) return;

            stageInTurn = 1;

            umaId = @event.data.chara_info.card_id + 1000000 * @event.data.chara_info.rarity;
            turn = @event.data.chara_info.turn - 1;
            vital = @event.data.chara_info.vital;
            maxVital = @event.data.chara_info.max_vital;
            isQieZhe = @event.data.chara_info.chara_effect_id_array.Contains(7);
            isAiJiao = @event.data.chara_info.chara_effect_id_array.Contains(8);
            failureRateBias = 0;
            if (@event.data.chara_info.chara_effect_id_array.Contains(6))
            {
                failureRateBias = 2;
            }
            if (@event.data.chara_info.chara_effect_id_array.Contains(10))
            {
                failureRateBias = -2;
            }


            fiveStatus = new int[]
            {
                ScoreUtils.ReviseOver1200(@event.data.chara_info.speed),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.stamina),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.power) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.guts) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.wiz) ,
            };

            fiveStatusLimit = new int[]
            {
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_speed),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_stamina),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_power) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_guts) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_wiz) ,
            };


            skillPt = @event.data.chara_info.skill_point;

            try
            {
                double ptRate = isQieZhe ? 2.0 : 1.8;
                double ptScore = AiUtils.calculateSkillScore(@event, ptRate);
                skillPt=(int)(ptScore/ptRate);
            }
            catch(Exception ex)
            {
                AnsiConsole.MarkupLine("获取当前技能分失败"+ex.Message);
            }

            motivation = @event.data.chara_info.motivation;

            cardId = new int[6];
            cardJiBan = new int[8];
            venusCardOutgoingUsed = new bool[] { false, false, false, false, false };

            foreach (var s in @event.data.chara_info.support_card_array)
            {
                int p = s.position - 1;
                //100000*突破数+卡原来的id，例如神团是30137，满破神团就是430137
                cardId[p] = s.limit_break_count * 100000 + s.support_card_id;
            }

            friendCardOutgoingStage = 0;
            friendCardUnlockOutgoing = false;

            venusCardUnlockOutgoing = false;
            venusCardOutgoingUsed =new bool[5] {false,false,false,false,false};
            venusCardIsQingRe = @event.data.chara_info.chara_effect_id_array.Contains(102);
            venusCardQingReContinuousTurns = 0;
            if(venusCardIsQingRe)
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
            
            foreach (var s in @event.data.chara_info.evaluation_info_array)
            {
                int p=s.target_id - 1;
                if (p < 6)//支援卡
                {
                    cardJiBan[p] = s.evaluation;
                    if (cardId[p] % 100000 == 30137)
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
                    if (cardId[p] % 100000 == 30160 || cardId[p] % 100000 == 10094)    // 佐岳
                    {
                        friendCardUnlockOutgoing = s.is_outing > 0;
                        friendCardOutgoingStage = s.story_step;
                    }

                }
                else if (p == 101)//理事长
                {
                    cardJiBan[6] = s.evaluation;
                }
                else if (p == 102)//记者
                {
                    cardJiBan[7] = s.evaluation;
                }
            }

            venusCardFirstClick = false;
            friendCardFirstClick = false;
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
            if (friendCardUnlockOutgoing)
                friendCardFirstClick = true;

            {
                //难以判断实际计数是多少，所以直接视为半级
                int[] trainLevelCountEachLevel = new int[6] { 0, 6, 18, 30, 42, 48 };
                trainLevelCount = new int[5];
                for (int i = 0; i < 5; i++)
                {
                    trainLevelCount[i] = trainLevelCountEachLevel[@event.data.chara_info.training_level_info_array.First(x => x.command_id == GameGlobal.TrainIds[i]).level];
                }
            }

            zhongMaBlueCount = new int[5];
            //用属性上限猜蓝因子个数
            {
                int[] defaultLimit = new int[5] { 1800, 1600, 1800, 1400, 1400 };
                double factor = 16;//每个三星因子可以提多少上限
                if (turn >= 54)//第二次继承结束
                    factor = 22;
                else if (turn >= 30)//第二次继承结束
                    factor = 19;
                for (int i = 0; i < 5; i++)
                {
                    int threeStarCount = (int)Math.Round((fiveStatusLimit[i] - defaultLimit[i]) / 2 / factor);
                    if (threeStarCount > 6) threeStarCount = 6;
                    if (threeStarCount < 0) threeStarCount = 0;
                    zhongMaBlueCount[i] = threeStarCount * 3;
                }
            }
            zhongMaExtraBonus = new int[6] { 20, 0, 40, 0, 20, 150 };//大师杯和青春杯因子各一半

            cardDistribution = new bool[5, 8];
            cardHint=new bool[6] {false,false,false,false,false,false};
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 8; j++)
                    cardDistribution[i, j] = false;
            isRacing = true;
            foreach (var train in @event.data.home_info.command_info_array)
            {
                if (!GameGlobal.ToTrainIndex.ContainsKey(train.command_id))//不是正常训练
                    continue;
                int trainId = GameGlobal.ToTrainIndex[train.command_id];
                if (train.is_enable > 0) isRacing = false;
                foreach(var p in train.training_partner_array)
                {
                    int pid = p <= 6 ? p - 1 : p == 102 ? 6 : 7;
                    cardDistribution[trainId, pid] = true;
                }
                foreach (var p in train.tips_event_partner_array)
                {
                    int pid = p - 1;
                    cardHint[pid] = true;
                }

            }

            // 计算碎片（女神杯）
            spiritDistribution = new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 };
            if (@event.IsScenario(ScenarioType.GrandMasters))
            {
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
            }
            trainValue = new int[5, 7];
            failRate = new int[5];
            {
                var currentVital = @event.data.chara_info.vital;
                //maxVital = @event.data.chara_info.max_vital;
                var currentFiveValue = fiveStatus;

                var trainItems = new Dictionary<int, SingleModeCommandInfo>();
                if (@event.IsScenario(ScenarioType.LArc))
                {
                    //LArc的合宿ID不一样，所以要单独处理
                    trainItems.Add(101, @event.data.home_info.command_info_array.Any(x => x.command_id == 1101) ? @event.data.home_info.command_info_array.First(x => x.command_id == 1101) : @event.data.home_info.command_info_array.First(x => x.command_id == 101));
                    trainItems.Add(105, @event.data.home_info.command_info_array.Any(x => x.command_id == 1102) ? @event.data.home_info.command_info_array.First(x => x.command_id == 1102) : @event.data.home_info.command_info_array.First(x => x.command_id == 105));
                    trainItems.Add(102, @event.data.home_info.command_info_array.Any(x => x.command_id == 1103) ? @event.data.home_info.command_info_array.First(x => x.command_id == 1103) : @event.data.home_info.command_info_array.First(x => x.command_id == 102));
                    trainItems.Add(103, @event.data.home_info.command_info_array.Any(x => x.command_id == 1104) ? @event.data.home_info.command_info_array.First(x => x.command_id == 1104) : @event.data.home_info.command_info_array.First(x => x.command_id == 103));
                    trainItems.Add(106, @event.data.home_info.command_info_array.Any(x => x.command_id == 1105) ? @event.data.home_info.command_info_array.First(x => x.command_id == 1105) : @event.data.home_info.command_info_array.First(x => x.command_id == 106));
                }
                else
                {
                    //速耐力根智，6xx为合宿时ID
                    trainItems.Add(101, @event.data.home_info.command_info_array.Any(x => x.command_id == 601) ? @event.data.home_info.command_info_array.First(x => x.command_id == 601) : @event.data.home_info.command_info_array.First(x => x.command_id == 101));
                    trainItems.Add(105, @event.data.home_info.command_info_array.Any(x => x.command_id == 602) ? @event.data.home_info.command_info_array.First(x => x.command_id == 602) : @event.data.home_info.command_info_array.First(x => x.command_id == 105));
                    trainItems.Add(102, @event.data.home_info.command_info_array.Any(x => x.command_id == 603) ? @event.data.home_info.command_info_array.First(x => x.command_id == 603) : @event.data.home_info.command_info_array.First(x => x.command_id == 102));
                    trainItems.Add(103, @event.data.home_info.command_info_array.Any(x => x.command_id == 604) ? @event.data.home_info.command_info_array.First(x => x.command_id == 604) : @event.data.home_info.command_info_array.First(x => x.command_id == 103));
                    trainItems.Add(106, @event.data.home_info.command_info_array.Any(x => x.command_id == 605) ? @event.data.home_info.command_info_array.First(x => x.command_id == 605) : @event.data.home_info.command_info_array.First(x => x.command_id == 106));
                }

                var trainStats = new TrainStats[5];
                var failureRate = new Dictionary<int, int>();
                for (int t = 0; t < 5; t++)
                {
                    int tid = GameGlobal.TrainIds[t];
                    failureRate[tid] = trainItems[tid].failure_rate;
                    var trainParams = new Dictionary<int, int>()
                {
                    {1,0},
                    {2,0},
                    {3,0},
                    {4,0},
                    {5,0},
                    {30,0},
                    {10,0},
                };
                    var nonScenarioTrainParams = new Dictionary<int, int>()
                {
                    {1,0},
                    {2,0},
                    {3,0},
                    {4,0},
                    {5,0},
                    {30,0},
                    {10,0},
                };
                    //去掉剧本加成的训练值（游戏里的下层显示）
                    foreach (var item in @event.data.home_info.command_info_array)
                    {
                        if (item.command_id == tid || item.command_id == GameGlobal.XiahesuIds[tid])
                        {
                            foreach (var trainParam in item.params_inc_dec_info_array)
                            {
                                nonScenarioTrainParams[trainParam.target_type] += trainParam.value;
                                trainParams[trainParam.target_type] += trainParam.value;
                            }
                        }
                    }

                    //青春杯
                    if (@event.data.team_data_set != null)
                    {
                        foreach (var item in @event.data.team_data_set.command_info_array)
                        {
                            if (item.command_id == tid || item.command_id == GameGlobal.XiahesuIds[tid])
                            {
                                foreach (var trainParam in item.params_inc_dec_info_array)
                                {
                                    trainParams[trainParam.target_type] += trainParam.value;
                                    //AnsiConsole.WriteLine($"{tid} {trainParam.target_type} {trainParam.value}");
                                }
                            }
                        }
                    }
                    //巅峰杯
                    if (@event.data.free_data_set != null)
                    {
                        foreach (var item in @event.data.free_data_set.command_info_array)
                        {
                            if (item.command_id == tid || item.command_id == GameGlobal.XiahesuIds[tid])
                            {
                                foreach (var trainParam in item.params_inc_dec_info_array)
                                {
                                    trainParams[trainParam.target_type] += trainParam.value;
                                    //AnsiConsole.WriteLine($"{tid} {trainParam.target_type} {trainParam.value}");
                                }
                            }
                        }
                    }
                    //偶像杯
                    if (@event.data.live_data_set != null)
                    {
                        foreach (var item in @event.data.live_data_set.command_info_array)
                        {
                            if (item.command_id == tid || item.command_id == GameGlobal.XiahesuIds[tid])
                            {
                                foreach (var trainParam in item.params_inc_dec_info_array)
                                {
                                    trainParams[trainParam.target_type] += trainParam.value;
                                    //AnsiConsole.WriteLine($"{tid} {trainParam.target_type} {trainParam.value}");
                                }
                            }
                        }
                    }
                    //女神杯
                    if (@event.IsScenario(ScenarioType.GrandMasters))
                    {
                        foreach (var item in @event.data.venus_data_set.command_info_array)
                        {
                            if (item.command_id == tid || item.command_id == GameGlobal.XiahesuIds[tid])
                            {
                                foreach (var trainParam in item.params_inc_dec_info_array)
                                {
                                    trainParams[trainParam.target_type] += trainParam.value;
                                }
                            }
                        }
                    }

                    //凯旋门
                    if (@event.IsScenario(ScenarioType.LArc))
                    {
                        larcData = new LArcDataSet(@event);
                        foreach (var item in @event.data.arc_data_set.command_info_array)
                        {
                            if (GameGlobal.ToTrainId.ContainsKey(item.command_id) &&
                                GameGlobal.ToTrainId[item.command_id] == tid)
                            {
                                foreach (var trainParam in item.params_inc_dec_info_array)
                                {
                                    trainParams[trainParam.target_type] += trainParam.value;
                                }
                            }
                        }
                    }

                    var stats = new TrainStats();
                    stats.FailureRate = trainItems[tid].failure_rate;
                    stats.VitalGain = trainParams[10];
                    if (currentVital + stats.VitalGain > maxVital)
                        stats.VitalGain = maxVital - currentVital;
                    if (stats.VitalGain < -currentVital)
                        stats.VitalGain = -currentVital;
                    stats.FiveValueGain = new int[] { trainParams[1], trainParams[2], trainParams[3], trainParams[4], trainParams[5] };
                    for (int i = 0; i < 5; i++)
                        stats.FiveValueGain[i] = ScoreUtils.ReviseOver1200(currentFiveValue[i] + stats.FiveValueGain[i]) - ScoreUtils.ReviseOver1200(currentFiveValue[i]);
                    stats.PtGain = trainParams[30];
                    stats.FiveValueGainNonScenario = new int[] { nonScenarioTrainParams[1], nonScenarioTrainParams[2], nonScenarioTrainParams[3], nonScenarioTrainParams[4], nonScenarioTrainParams[5] };
                    for (int i = 0; i < 5; i++)
                        stats.FiveValueGainNonScenario[i] = ScoreUtils.ReviseOver1200(currentFiveValue[i] + stats.FiveValueGainNonScenario[i]) - ScoreUtils.ReviseOver1200(currentFiveValue[i]);
                    stats.PtGainNonScenario = nonScenarioTrainParams[30];


                    for (int i = 0; i < 5; i++)
                        trainValue[t, i] = stats.FiveValueGain[i];
                    trainValue[t, 5] = stats.PtGain;
                    trainValue[t, 6] = stats.VitalGain;
                    failRate[t] = stats.FailureRate;
                }

            }

        }

    }
}
