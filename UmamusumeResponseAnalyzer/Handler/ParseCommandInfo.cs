using Gallop;
using Spectre.Console;
using System.Text.RegularExpressions;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.AI;
using MessagePack;
using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel.Design;
using System.IO.Pipes;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using UmamusumeResponseAnalyzer.Communications.Subscriptions;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static async void ParseCommandInfo(Gallop.SingleModeCheckEventResponse @event)
        {
            if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0) || @event.data.race_start_info != null) return;
            SubscribeCommandInfo.Signal(@event);
            var turnNum = @event.data.chara_info.turn;
            var LArcIsAbroad = (turnNum >= 37 && turnNum <= 43) || (turnNum >= 61 && turnNum <= 67);

            var currentFiveValue = new int[]
            {
                @event.data.chara_info.speed,
                @event.data.chara_info.stamina,
                @event.data.chara_info.power ,
                @event.data.chara_info.guts ,
                @event.data.chara_info.wiz ,
            };
            var fiveValueMaxRevised = new int[]
            {
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_speed),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_stamina),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_power) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_guts) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_wiz) ,
            };
            var currentFiveValueRevised = currentFiveValue.Select(x => ScoreUtils.ReviseOver1200(x)).ToArray();
            var totalValue = currentFiveValueRevised.Sum();
            AnsiConsole.WriteLine(string.Empty);

            if (GameStats.currentTurn != turnNum - 1 //正常情况
                && GameStats.currentTurn != turnNum //重复显示
                && turnNum != 1 //第一个回合
                )
            {
                GameStats.isFullGame = false;
                AnsiConsole.MarkupLine($"[red]警告：回合数不正确，上一个回合为{GameStats.currentTurn}，当前回合为{turnNum}[/]");
                EventLogger.Init(@event.data.chara_info.support_card_array);
            }
            else if (turnNum == 1)
            {
                GameStats.isFullGame = true;
                EventLogger.Init(@event.data.chara_info.support_card_array);
            }

            //买技能，大师杯剧本年末比赛，会重复显示
            var isRepeat = @event.data.chara_info.playing_state != 1;

            //初始化TurnStats
            if (isRepeat)
            {
                AnsiConsole.MarkupLine($"[yellow]******此回合为重复显示******[/]");
            }
            else
            {
                GameStats.whichScenario = @event.data.chara_info.scenario_id;
                GameStats.currentTurn = turnNum;
                GameStats.stats[turnNum] = new TurnStats();
            }

            #region 事件监测
            if (!isRepeat)
                EventLogger.Update(@event);
            #endregion

            //为了避免写判断，对于重复回合，直接让turnStat指向一个无用的TurnStats类
            var turnStat = isRepeat ? new TurnStats() : GameStats.stats[turnNum];
            var gameYear = (turnNum - 1) / 24 + 1;
            var gameMonth = ((turnNum - 1) % 24) / 2 + 1;
            var halfMonth = (turnNum % 2 == 0) ? "后半" : "前半";
            var totalTurns = @event.IsScenario(ScenarioType.LArc) ? 67 : 78;

            AnsiConsole.MarkupLine($"[#00ffff]------------------------------------------------------------------------------------[/]");
            AnsiConsole.MarkupLine($"[green]回合数：{@event.data.chara_info.turn}/{totalTurns}, 第{gameYear}年{gameMonth}月{halfMonth}[/]");

            var motivation = @event.data.chara_info.motivation;
            turnStat.motivation = motivation;
            //显示统计信息
            GameStats.Print();

            var currentVital = @event.data.chara_info.vital;
            var maxVital = @event.data.chara_info.max_vital;
            switch (currentVital)
            {
                case < 30:
                    AnsiConsole.MarkupLine($"[red]体力：{currentVital}[/]/{maxVital}");
                    break;
                case < 50:
                    AnsiConsole.MarkupLine($"[darkorange]体力：{currentVital}[/]/{maxVital}");
                    break;
                case < 70:
                    AnsiConsole.MarkupLine($"[yellow]体力：{currentVital}[/]/{maxVital}");
                    break;
                default:
                    AnsiConsole.MarkupLine($"[green]体力：{currentVital}[/]/{maxVital}");
                    break;
            }

            switch (motivation)
            {
                case 5:
                    AnsiConsole.MarkupLine($"干劲[green]绝好调[/]");
                    break;
                case 4:
                    AnsiConsole.MarkupLine($"干劲[yellow]好调[/]");
                    break;
                case 3:
                    AnsiConsole.MarkupLine($"干劲[red]普通[/]");
                    break;
                case 2:
                    AnsiConsole.MarkupLine($"干劲[red]不调[/]");
                    break;
                case 1:
                    AnsiConsole.MarkupLine($"干劲[red]绝不调[/]");
                    break;
            }

            var totalValueWithPt = totalValue + @event.data.chara_info.skill_point;
            var totalValueWithHalfPt = totalValue + 0.5 * @event.data.chara_info.skill_point;
            AnsiConsole.MarkupLine($"[aqua]总属性：{totalValue}[/]\t[aqua]总属性+0.5*pt：{totalValueWithHalfPt}[/]");

            #region LArc
            //计算训练等级
            if (@event.IsScenario(ScenarioType.LArc))//预测训练等级
            {
                for (var i = 0; i < 5; i++)
                {
                    if (turnNum == 1)
                    {
                        turnStat.trainLevel[i] = 1;
                        turnStat.trainLevelCount[i] = 0;
                    }
                    else
                    {
                        var lastTrainLevel = GameStats.stats[turnNum - 1] != null ? GameStats.stats[turnNum - 1].trainLevel[i] : 1;
                        var lastTrainLevelCount = GameStats.stats[turnNum - 1] != null ? GameStats.stats[turnNum - 1].trainLevelCount[i] : 0;

                        turnStat.trainLevel[i] = lastTrainLevel;
                        turnStat.trainLevelCount[i] = lastTrainLevelCount;
                        if (GameStats.stats[turnNum - 1] != null &&
                            GameStats.stats[turnNum - 1].playerChoice == GameGlobal.TrainIds[i] &&
                            !GameStats.stats[turnNum - 1].isTrainingFailed &&
                            !((turnNum - 1 >= 37 && turnNum - 1 <= 43) || (turnNum - 1 >= 61 && turnNum - 1 <= 67))
                            )//上回合点的这个训练，计数+1
                            turnStat.trainLevelCount[i] += 1;
                        if (turnStat.trainLevelCount[i] >= 4)
                        {
                            turnStat.trainLevelCount[i] -= 4;
                            turnStat.trainLevel[i] += 1;
                        }
                        //检查是否有期待度上升
                        var appRate = @event.data.arc_data_set.arc_info.approval_rate;
                        var oldAppRate = GameStats.stats[turnNum - 1] != null ? (GameStats.stats[turnNum - 1].larc_totalApproval + 85) / 170 : 0;
                        if (oldAppRate < 200 && appRate >= 200)
                            turnStat.trainLevel[i] += 1;
                        if (oldAppRate < 600 && appRate >= 600)
                            turnStat.trainLevel[i] += 1;
                        if (oldAppRate < 1000 && appRate >= 1000)
                            turnStat.trainLevel[i] += 1;

                        if (turnStat.trainLevel[i] >= 5)
                        {
                            turnStat.trainLevel[i] = 5;
                            turnStat.trainLevelCount[i] = 0;
                        }
                    }
                }
            }
            //额外显示LArc信息
            if (@event.IsScenario(ScenarioType.LArc))
            {
                turnStat.larc_isSSS = @event.data.arc_data_set.selection_info?.is_special_match == 1;
                turnStat.larc_totalApproval = @event.data.arc_data_set.arc_rival_array.Sum(x => x.approval_point);
                var totalSSLevel = @event.data.arc_data_set.arc_rival_array.Sum(x => x.star_lv);
                var rivalBoostCount = new int[] { 0, 0, 0, 0 };
                var effectCount = new int[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                foreach (var rival in @event.data.arc_data_set.arc_rival_array)
                {
                    if (rival.selection_peff_array == null)
                        continue; ///马娘自身
                    rivalBoostCount[rival.rival_boost] += 1;
                    foreach (var ef in rival.selection_peff_array)
                    {
                        effectCount[ef.effect_group_id] += 1;
                    }
                }
                var approval_rate = @event.data.arc_data_set.arc_info.approval_rate;
                var approval_rate_level = approval_rate / 50;
                var approval_training_bonus = GameGlobal.LArcTrainBonusEvery5Percent[approval_rate_level > 40 ? 40 : approval_rate_level];
                var lastTurnTotalApproval = GameStats.stats[turnNum - 1] != null ? GameStats.stats[turnNum - 1].larc_totalApproval : 0;
                AnsiConsole.MarkupLine($"期待度：[#00ff00]{approval_rate / 10}.{approval_rate % 10}%[/]（训练[#00ffff]+{approval_training_bonus}%[/]）    适性pt：[#00ff00]{@event.data.arc_data_set.arc_info.global_exp}[/]    总支援pt：[#00ff00]{turnStat.larc_totalApproval}[/]([aqua]+{turnStat.larc_totalApproval - lastTurnTotalApproval}[/])");

                int totalCount = totalSSLevel * 3 + rivalBoostCount[1] * 1 + rivalBoostCount[2] * 2 + rivalBoostCount[3] * 3;
                AnsiConsole.MarkupLine($"总格数：[#00ff00]{totalCount}[/]    总SS数：[#00ff00]{totalSSLevel}[/]    0123格：[aqua]{rivalBoostCount[0]} {rivalBoostCount[1]} {rivalBoostCount[2]} [/][#00ff00]{rivalBoostCount[3]}[/]");

                var toPrint = string.Empty;
                //每个人头（包括支援卡）每3级一定有一个属性，一个pt，一个特殊词条。其中特殊词条在一局内是固定的
                //每局15个人头的每种特殊词条的总数是固定的。但是除了几个特殊的（体力最大值-茶座、爱娇-黄金船、练习上手-神鹰），其他都会随机分配给支援卡和路人
                //支援卡相比路人点的次数更多，如果第三回合的支援卡随机分配的特殊词条不好，就可以重开了

                if (turnNum <= 2)
                {
                    //清空SSRivalsSpecialBuffs
                    GameStats.SSRivalsSpecialBuffs.Clear();
                }
                else
                {
                    GameStats.SSRivalsSpecialBuffs[1014] = 9;
                    GameStats.SSRivalsSpecialBuffs[1007] = 8;

                    foreach (var arc_data in @event.data.arc_data_set.arc_rival_array)
                    {
                        if (arc_data.selection_peff_array == null)//马娘自身
                            continue;

                        if (!GameStats.SSRivalsSpecialBuffs.ContainsKey(arc_data.chara_id))
                            GameStats.SSRivalsSpecialBuffs[arc_data.chara_id] = 0; //未知状态

                        foreach (var ef in arc_data.selection_peff_array)
                        {
                            var efid = ef.effect_group_id;
                            if (efid != 1 && efid != 11) //特殊buff
                            {
                                var efid_old = GameStats.SSRivalsSpecialBuffs[arc_data.chara_id];
                                if (efid_old == 0)
                                    GameStats.SSRivalsSpecialBuffs[arc_data.chara_id] = efid;
                                else if (efid_old != efid)//要么是出错，要么是神鹰的练习上手+适性pt
                                {
                                    if (efid_old == 7 && efid == 9)
                                    {
                                        GameStats.SSRivalsSpecialBuffs[arc_data.chara_id] = 9;
                                    }
                                    else if (efid_old == 9 && efid == 7)
                                    {
                                        //什么都不用做
                                    }
                                    else
                                    {
                                        AnsiConsole.MarkupLine($"[red]警告：larc的ss特殊buff错误，{arc_data.chara_id} {efid} {efid_old}[/]");
                                    }
                                }
                            }
                        }
                    }
                }

                var supportCards1 = @event.data.chara_info.support_card_array.ToDictionary(x => x.position, x => x.support_card_id); //当前S卡卡组
                for (int cardCount = 0; cardCount < 8; cardCount++)
                {
                    if (supportCards1.Any(x => x.Key == cardCount))
                    {

                        var name = Database.Names.GetCharacter(supportCards1[cardCount]).Nickname.EscapeMarkup(); //partner是当前S卡卡组的index（1~6，7是啥？我忘了）或者charaId（10xx)
                        var charaTrainingType = string.Empty;
                        var specialBuffs = string.Empty;
                        var chara_id = @event.data.arc_data_set.evaluation_info_array.First(x => x.target_id == cardCount).chara_id;
                        if (@event.data.arc_data_set.arc_rival_array.Any(x => x.chara_id == chara_id))
                        {
                            var arc_data = @event.data.arc_data_set.arc_rival_array.First(x => x.chara_id == chara_id);

                            charaTrainingType = $"[red]({GameGlobal.TrainNames[arc_data.command_id]})[/]";

                            if (GameStats.SSRivalsSpecialBuffs[arc_data.chara_id] != 0)
                                specialBuffs = GameGlobal.LArcSSEffectNameFullColored[GameStats.SSRivalsSpecialBuffs[arc_data.chara_id]];
                            else
                                specialBuffs = "?";
                        }
                        toPrint += $"{name}:{charaTrainingType}{specialBuffs} ";
                    }
                }
                AnsiConsole.MarkupLine(toPrint);
                //凯旋门前显示技能性价比
                if (turnNum == 43 || turnNum == 67)
                {
                    var tips = CalculateSkillScoreCost(@event, Database.Skills.Apply(@event.data.chara_info), false);

                    var table1 = new Table();
                    table1.Title("技能性价比排序");
                    table1.AddColumns("技能名称", "pt", "评分", "性价比");
                    table1.Columns[0].Centered();
                    foreach (var i in tips
                        // (string Name, int Cost, int Grade, double Cost-Performance)
                        .Where(x => x.Cost != int.MaxValue)
                        .Select(tip => (tip.Name, tip.Cost, tip.Grade, (double)tip.Grade / tip.Cost))
                        .OrderByDescending(x => x.Item4))
                    {
                        table1.AddRow($"{i.Name}", $"{i.Cost}", $"{i.Grade}", $"{i.Item4:F3}");
                    }

                    AnsiConsole.Write(table1);
                }
            }
            #endregion

            #region Grand Masters
            //额外显示GM杯信息
            if (@event.IsScenario(ScenarioType.GrandMasters))
            {
                var outputLine = "当前碎片组：";
                var spiritColors = new int[8]; //0空，1红，2蓝，3黄
                for (var spiritPlace = 1; spiritPlace < 9; spiritPlace++)
                {
                    var spiritId =
                        @event.data.venus_data_set.spirit_info_array.Any(x => x.spirit_num == spiritPlace)
                        ? @event.data.venus_data_set.spirit_info_array.First(x => x.spirit_num == spiritPlace).spirit_id
                        : -1;
                    spiritColors[spiritPlace - 1] = (8 + spiritId) / 8;  //0空，1红，2蓝，3黄
                    if (GameGlobal.GrandMastersSpiritNamesColored.TryGetValue(spiritId, out var spiritStr))
                    {
                        outputLine += (spiritPlace == 1 || spiritPlace == 5) ? $"{{{spiritStr}}} " : $"{spiritStr} ";
                    }
                }
                AnsiConsole.MarkupLine(outputLine);

                //看看有没有凑齐的女神
                if (@event.data.venus_data_set.spirit_info_array.Any(x => x.spirit_id == 9040)) AnsiConsole.MarkupLine("当前女神睿智：[red]红[/]");
                else if (@event.data.venus_data_set.spirit_info_array.Any(x => x.spirit_id == 9041)) AnsiConsole.MarkupLine("当前女神睿智：[blue]蓝[/]");
                else if (@event.data.venus_data_set.spirit_info_array.Any(x => x.spirit_id == 9042)) AnsiConsole.MarkupLine("当前女神睿智：[yellow]黄[/]");
                else //预测下一个女神
                {
                    var colorStrs = new string[] { "⚪", "[red]红[/]", "[blue]蓝[/]", "[yellow]黄[/]" };
                    if (spiritColors[0] == 0)
                    {
                        AnsiConsole.MarkupLine("下一个女神：⚪ [green]vs[/] ⚪");
                    }
                    else if (spiritColors[0] != 0 && spiritColors[4] == 0)
                    {
                        var color1 = spiritColors[0];
                        var color1count = spiritColors.Count(x => x == color1);
                        AnsiConsole.MarkupLine($"下一个女神：{colorStrs[color1]}x{color1count} [green]vs[/] ⚪");
                    }
                    else
                    {
                        int color1 = spiritColors[0];
                        int color1count = spiritColors.Count(x => x == color1);
                        int color2 = spiritColors[4];
                        int color2count = spiritColors.Count(x => x == color2);
                        int emptycount = spiritColors.Count(x => x == 0);
                        if (color1 == color2 || color1count > color2count + emptycount)
                            AnsiConsole.MarkupLine($"下一个女神：{colorStrs[color1]}");
                        else if (color2count > color1count + emptycount)
                            AnsiConsole.MarkupLine($"下一个女神：{colorStrs[color2]}");
                        else
                            AnsiConsole.MarkupLine($"下一个女神：{colorStrs[color1]}x{color1count} [green]vs[/] {colorStrs[color2]}x{color2count}");
                    }
                }

                if (@event.data.venus_data_set.venus_chara_info_array != null && @event.data.venus_data_set.venus_chara_info_array.Any(x => x.chara_id == 9042))
                {
                    var venusLevels = @event.data.venus_data_set.venus_chara_info_array;
                    turnStat.venus_yellowVenusLevel = venusLevels.First(x => x.chara_id == 9042).venus_level;
                    turnStat.venus_redVenusLevel = venusLevels.First(x => x.chara_id == 9040).venus_level;
                    turnStat.venus_blueVenusLevel = venusLevels.First(x => x.chara_id == 9041).venus_level;
                    AnsiConsole.MarkupLine($"女神等级：" +
                        $"[yellow]{turnStat.venus_yellowVenusLevel}[/] " +
                        $"[red]{turnStat.venus_redVenusLevel}[/] " +
                        $"[blue]{turnStat.venus_blueVenusLevel}[/] "
                        );
                }
                // 是否开蓝了
                if (@event.data.venus_data_set.venus_spirit_active_effect_info_array.Any(x => x.chara_id == 9041))
                {
                    turnStat.venus_isVenusCountConcerned = false;
                }
            }
            //女神情热状态，不统计女神召唤次数
            if (@event.data.chara_info.chara_effect_id_array.Any(x => x == 102))
            {
                turnStat.venus_isVenusCountConcerned = false;
                turnStat.venus_isEffect102 = true;
                //统计一下女神情热持续了几回合
                var continuousTurnNum = 0;
                for (var i = turnNum; i >= 1; i--)
                {
                    if (GameStats.stats[i] == null || !GameStats.stats[i].venus_isEffect102)
                        break;
                    continuousTurnNum++;
                }
                AnsiConsole.MarkupLine($"女神彩圈已持续[green]{continuousTurnNum}[/]回合");
            }
            #endregion

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
            for (var i = 0; i < 5; i++)
            {
                var trainId = GameGlobal.TrainIds[i];
                failureRate[trainId] = trainItems[trainId].failure_rate;
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
                dynamic commandInfoArray = @event.data.home_info.command_info_array;
                //去掉剧本加成的训练值（游戏里的下层显示）
                foreach (var item in commandInfoArray)
                    if (GameGlobal.ToTrainId.TryGetValue(item.command_id, out int value) && value == trainId)
                        foreach (var trainParam in item.params_inc_dec_info_array)
                            trainParams[trainParam.target_type] += trainParam.value;
                var nonScenarioTrainParams = new Dictionary<int, int>(trainParams);
                if (@event.data.team_data_set != null) // 青春杯
                    commandInfoArray = @event.data.team_data_set.command_info_array;
                else if (@event.data.free_data_set != null) // 巅峰杯
                    commandInfoArray = @event.data.free_data_set.command_info_array;
                else if (@event.data.live_data_set != null) // 偶像杯
                    commandInfoArray = @event.data.live_data_set.command_info_array;
                else if (@event.IsScenario(ScenarioType.GrandMasters)) // 女神杯
                    commandInfoArray = @event.data.venus_data_set.command_info_array;
                else if (@event.IsScenario(ScenarioType.LArc)) // 凯旋门
                    commandInfoArray = @event.data.arc_data_set.command_info_array;
                if (commandInfoArray is System.Collections.IEnumerable and not null)
                    foreach (var item in commandInfoArray)
                        if (GameGlobal.ToTrainId.TryGetValue(item.command_id, out int value) && value == trainId)
                            foreach (var trainParam in item.params_inc_dec_info_array)
                                trainParams[trainParam.target_type] += trainParam.value;

                var stats = new TrainStats
                {
                    FailureRate = trainItems[trainId].failure_rate,
                    VitalGain = trainParams[10]
                };
                if (currentVital + stats.VitalGain > maxVital)
                    stats.VitalGain = maxVital - currentVital;
                if (stats.VitalGain < -currentVital)
                    stats.VitalGain = -currentVital;
                stats.FiveValueGain = [trainParams[1], trainParams[2], trainParams[3], trainParams[4], trainParams[5]];
                for (var j = 0; j < 5; j++)
                    stats.FiveValueGain[j] = ScoreUtils.ReviseOver1200(currentFiveValue[j] + stats.FiveValueGain[j]) - ScoreUtils.ReviseOver1200(currentFiveValue[j]);
                stats.PtGain = trainParams[30];
                stats.FiveValueGainNonScenario = [nonScenarioTrainParams[1], nonScenarioTrainParams[2], nonScenarioTrainParams[3], nonScenarioTrainParams[4], nonScenarioTrainParams[5]];
                for (var j = 0; j < 5; j++)
                    stats.FiveValueGainNonScenario[j] = ScoreUtils.ReviseOver1200(currentFiveValue[j] + stats.FiveValueGainNonScenario[j]) - ScoreUtils.ReviseOver1200(currentFiveValue[j]);
                stats.PtGainNonScenario = nonScenarioTrainParams[30];
                trainStats[i] = stats;
            }
            turnStat.fiveTrainStats = trainStats;
            var table = new Table();

            var failureRateStr = new string[5];
            //失败率>=40%标红、>=20%(有可能大失败)标DarkOrange、>0%标黄
            for (int i = 0; i < 5; i++)
            {
                int thisFailureRate = failureRate[GameGlobal.TrainIds[i]];
                failureRateStr[i] = thisFailureRate switch
                {
                    >= 40 => $"[red]({thisFailureRate}%)[/]",
                    >= 20 => $"[darkorange]({thisFailureRate}%)[/]",
                    > 0 => $"[yellow]({thisFailureRate}%)[/]",
                    _ => string.Empty
                };
            }
            table.AddColumns(
                  new TableColumn($"速{failureRateStr[0]}").Width(15)
                , new TableColumn($"耐{failureRateStr[1]}").Width(15)
                , new TableColumn($"力{failureRateStr[2]}").Width(15)
                , new TableColumn($"根{failureRateStr[3]}").Width(15)
                , new TableColumn($"智{failureRateStr[4]}").Width(15));
            if (@event.IsScenario(ScenarioType.LArc))
            {
                table.AddColumn(new TableColumn("SS Match").Width(15));
            }
            var separatorLine = Enumerable.Repeat(new string(Enumerable.Repeat('-', table.Columns.Max(x => x.Width.GetValueOrDefault())).ToArray()), 5).ToArray();
            var separatorLineSSMatch = new string(Enumerable.Repeat('-', 15).ToArray());

            var outputItems = new string[5];
            table.AddToRows(0, Enumerable.Repeat("当前:可获得", 5).ToArray());
            //显示此属性的当前属性及还差多少属性达到上限
            for (var i = 0; i < 5; i++)
            {
                var remainValue = fiveValueMaxRevised[i] - currentFiveValueRevised[i];
                outputItems[i] = remainValue switch
                {
                    > 400 => $"{currentFiveValueRevised[i]}: {remainValue}属性",
                    > 200 => $"{currentFiveValueRevised[i]}: [yellow]{remainValue}[/]属性",
                    _ => $"{currentFiveValueRevised[i]}: [red]{remainValue}[/]属性"
                };
            }
            table.AddToRows(1, outputItems);
            table.AddToRows(2, separatorLine);
            //显示训练后的剩余体力
            for (int i = 0; i < 5; i++)
            {
                int tid = GameGlobal.TrainIds[i];
                var VitalGain = trainStats[i].VitalGain;
                var newVital = VitalGain + currentVital;
                outputItems[i] = newVital switch
                {
                    < 30 => $"体力:[red]{newVital}[/]/{maxVital}",
                    < 50 => $"体力:[darkorange]{newVital}[/]/{maxVital}",
                    < 70 => $"体力:[yellow]{newVital}[/]/{maxVital}",
                    _ => $"体力:[green]{newVital}[/]/{maxVital}"
                };
            }
            table.AddToRows(3, outputItems);

            //显示此训练的训练等级
            for (var i = 0; i < 5; i++)
            {
                var normalId = GameGlobal.TrainIds[i];
                if (@event.data.home_info.command_info_array.Any(x => x.command_id == GameGlobal.XiahesuIds[normalId]))
                {
                    outputItems[i] = "[green]夏合宿[/]";
                }
                else if (@event.IsScenario(ScenarioType.LArc) && LArcIsAbroad)
                {
                    outputItems[i] = "[green]远征[/]";
                }
                else
                {
                    var lv = @event.data.chara_info.training_level_info_array.First(x => x.command_id == normalId).level;
                    if (@event.IsScenario(ScenarioType.LArc) && turnStat.trainLevel[i] != lv && !isRepeat)
                    {
                        //可能是半途开启小黑板，也可能是有未知bug
                        AnsiConsole.MarkupLine($"[red]警告：训练等级预测错误，预测{GameGlobal.TrainNames[normalId]}为lv{turnStat.trainLevel[i]}(+{turnStat.trainLevelCount[i]})，实际为lv{lv}[/]");
                        turnStat.trainLevel[i] = lv;
                        turnStat.trainLevelCount[i] = 0;//如果是半途开启小黑板，则会在下一次升级时变成正确的计数
                    }
                    if (@event.IsScenario(ScenarioType.LArc))
                        outputItems[i] = lv < 5 ? $"[yellow]Lv{lv}[/](+{turnStat.trainLevelCount[i]})" : $"Lv{lv}";
                    else
                        outputItems[i] = lv < 5 ? $"[yellow]Lv{lv}[/]" : $"Lv{lv}";
                }
            }
            table.AddToRows(4, outputItems);
            table.AddToRows(5, separatorLine);

            //显示此次训练可获得的属性和Pt
            var bestScore = -100;
            var bestTrain = -1;
            for (var i = 0; i < 5; i++)
            {
                var tid = GameGlobal.TrainIds[i];
                var stats = trainStats[i];
                var score = stats.FiveValueGain.Sum();
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTrain = i;
                }
                outputItems[i] = $"{score}";
            }
            for (int i = 0; i < 5; i++)
            {
                if (i == bestTrain)
                    outputItems[i] = $"属性:[aqua]{outputItems[i]}[/]|Pt:{trainStats[i].PtGain}";
                else
                    outputItems[i] = $"属性:[green]{outputItems[i]}[/]|Pt:{trainStats[i].PtGain}";
            }
            table.AddToRows(6, outputItems);

            //以下几项用于计算单次训练能充多少格
            var LArcRivalBoostCount = new int[,] { { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 } };// 五种训练的充电槽为0,1,2格的个数
            var LArcShiningCount = new int[] { 0, 0, 0, 0, 0 };//彩圈个数
            var LArcfriendAppear = new bool[] { false, false, false, false, false };//友人在不在

            // 当前S卡卡组
            var supportCards = @event.data.chara_info.support_card_array.ToDictionary(x => x.position, x => x.support_card_id);
            var commandInfo = new Dictionary<int, string[]>();
            foreach (var command in @event.data.home_info.command_info_array)
            {
                if (!GameGlobal.ToTrainIndex.ContainsKey(command.command_id)) continue;
                var trainIdx = GameGlobal.ToTrainIndex[command.command_id];

                var tips = command.tips_event_partner_array.Intersect(command.training_partner_array); //红感叹号 || Hint
                var partners = command.training_partner_array
                    .Select(partner =>
                    {
                        turnStat.isTraining = true;
                        var priority = PartnerPriority.默认;

                        // partner是当前S卡卡组的index（1~6，7是啥？我忘了）或者charaId（10xx)
                        var name = (partner >= 1 && partner <= 7 ? Database.Names.GetSupportCard(supportCards[partner]).Nickname : Database.Names.GetCharacter(partner).Nickname).EscapeMarkup();
                        var friendship = @event.data.chara_info.evaluation_info_array.First(x => x.target_id == partner).evaluation;
                        bool isArcPartner = @event.IsScenario(ScenarioType.LArc) && (partner > 1000 || (partner >= 1 && partner <= 7)) && @event.data.arc_data_set.evaluation_info_array.Any(x => x.target_id == partner);
                        var nameColor = "[#ffffff]";
                        var nameAppend = "";
                        bool shouldShining = false; // 是不是友情训练
                        if (partner >= 1 && partner <= 7)
                        {
                            priority = PartnerPriority.其他;
                            if (name.Contains("[友]")) // 友人单独标绿
                            {
                                priority = PartnerPriority.友人;
                                nameColor = $"[green]";

                                switch (supportCards[partner])
                                {
                                    case 30137: // 三女神团队卡的友情训练
                                        turnStat.venus_venusTrain = GameGlobal.ToTrainId[command.command_id];
                                        break;
                                    case 30160 or 10094: // 佐岳友人卡
                                        LArcfriendAppear[trainIdx] = true;
                                        turnStat.larc_zuoyueAtTrain[trainIdx] = true;
                                        break;
                                    case 30188 or 10104:    // 都留岐涼花
                                        turnStat.uaf_friendAtTrain[trainIdx] = true;
                                        break;
                                }
                            }
                            else if (friendship < 80) // 羁绊不满80，无法触发友情训练标黄
                            {
                                priority = PartnerPriority.羁绊不足;
                                nameColor = $"[yellow]";
                            }

                            //闪彩标蓝
                            {
                                //在得意位置上
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
                                //GM杯检查
                                if (@event.IsScenario(ScenarioType.GrandMasters) && @event.data.venus_data_set.venus_spirit_active_effect_info_array.Any(x => x.chara_id == 9042 && x.effect_group_id == 421)
                                    && (name.Contains("[速]") || name.Contains("[耐]") || name.Contains("[力]") || name.Contains("[根]") || name.Contains("[智]")))
                                {
                                    shouldShining = true;
                                }

                                if ((supportCards[partner] == 30137 && @event.data.chara_info.chara_effect_id_array.Any(x => x == 102)) || //神团
                                (supportCards[partner] == 30067 && @event.data.chara_info.chara_effect_id_array.Any(x => x == 101)) || //皇团
                                (supportCards[partner] == 30081 && @event.data.chara_info.chara_effect_id_array.Any(x => x == 100)) //天狼星
                                )
                                {
                                    shouldShining = true;
                                    nameColor = $"[#80ff00]";
                                }
                            }

                            if (shouldShining)
                            {
                                LArcShiningCount[trainIdx] += 1;
                                if (name.Contains("[友]"))
                                {
                                    priority = PartnerPriority.友人;
                                    nameColor = $"[#80ff00]";
                                }
                                else
                                {
                                    priority = PartnerPriority.闪;
                                    nameColor = $"[aqua]";
                                }
                            }
                        }
                        else
                        {
                            if (partner >= 100 && partner < 1000)//理事长、记者等
                            {
                                priority = PartnerPriority.关键NPC;
                                nameColor = $"[#008080]";
                            }
                            else if (isArcPartner) // 凯旋门的其他人
                            {
                                priority = PartnerPriority.无用NPC;
                                nameColor = $"[#a166ff]";
                            }
                        }

                        if ((partner >= 1 && partner <= 7) || (partner >= 100 && partner < 1000))//支援卡，理事长，记者，佐岳
                            if (friendship < 100) //羁绊不满100，显示羁绊
                                nameAppend += $"[red]{friendship}[/]";

                        if (isArcPartner && !LArcIsAbroad)
                        {
                            var chara_id = @event.data.arc_data_set.evaluation_info_array.First(x => x.target_id == partner).chara_id;
                            if (@event.data.arc_data_set.arc_rival_array.Any(x => x.chara_id == chara_id))
                            {
                                var arc_data = @event.data.arc_data_set.arc_rival_array.First(x => x.chara_id == chara_id);
                                var rival_boost = arc_data.rival_boost;
                                var effectId = arc_data.selection_peff_array.First(x => x.effect_num == arc_data.selection_peff_array.Min(x => x.effect_num)).effect_group_id;
                                if (rival_boost != 3)
                                {
                                    if (priority > PartnerPriority.需要充电) priority = PartnerPriority.需要充电;
                                    LArcRivalBoostCount[trainIdx, rival_boost] += 1;

                                    if (partner > 1000)
                                        nameColor = $"[#ff00ff]";
                                    nameAppend += $":[aqua]{rival_boost}[/]{GameGlobal.LArcSSEffectNameColoredShort[effectId]}";
                                }
                            }
                        }

                        name = $"{nameColor}{name}[/]{nameAppend}";
                        name = tips.Contains(partner) ? $"[red]![/]{name}" : name; //有Hint就加个红感叹号，和游戏内表现一样

                        return (priority, name);
                    }).ToArray();

                // 按照优先级排序
                commandInfo.Add(command.command_id, partners.OrderBy(s => s.priority).Select(x => x.name).ToArray());
            }
            if (!commandInfo.SelectMany(x => x.Value).Any()) return;
            //LArc充电槽计数
            if (@event.IsScenario(ScenarioType.LArc) && !LArcIsAbroad)
            {
                for (var i = 0; i < 5; i++)
                {
                    var chargedNum = LArcRivalBoostCount[i, 0] + LArcRivalBoostCount[i, 1] + LArcRivalBoostCount[i, 2];
                    var chargedFullNum = LArcRivalBoostCount[i, 2];
                    if (LArcShiningCount[i] >= 1)
                    {
                        chargedNum += LArcRivalBoostCount[i, 0] + LArcRivalBoostCount[i, 1];
                        chargedFullNum += LArcRivalBoostCount[i, 1];
                    }
                    if (LArcShiningCount[i] >= 2)
                    {
                        chargedNum += LArcRivalBoostCount[i, 0];
                        chargedFullNum += LArcRivalBoostCount[i, 0];
                    }
                    outputItems[i] = $"格数[#00ff00]{chargedNum}{(LArcfriendAppear[i] ? "+友" : string.Empty)}[/]|满数[#00ff00]{chargedFullNum}[/]";
                }
                table.AddToRows(7, outputItems);
            }

            table.AddToRows(8, separatorLine);
            for (var i = 0; i < 5; ++i)
            {
                table.AddToRows(9 + i, commandInfo.Select(x => x.Value.Length > i ? x.Value[i] : string.Empty).ToArray());//第8行预留位置
            }

            if (@event.IsScenario(ScenarioType.MakeANewTrack) && @event.data.free_data_set != null)
            {
                var freeDataSet = @event.data.free_data_set;
                var coinNum = freeDataSet.coin_num;
                var inventory = freeDataSet.user_item_info_array?.ToDictionary(x => x.item_id, x => x.num) ?? [];
                var shouldPromoteTea = inventory.ContainsKey(2301) ||  //包里或者商店里有加干劲的道具
                    inventory.ContainsKey(2302) ||
                    freeDataSet.pick_up_item_info_array.Any(x => x.item_id == 2301) ||
                    freeDataSet.pick_up_item_info_array.Any(x => x.item_id == 2302);
                var currentTurn = @event.data.chara_info.turn;

                var rows = new List<List<string>> { new(), new(), new(), new(), new() };
                var k = 0;
                foreach (var j in freeDataSet.pick_up_item_info_array
                    .Where(x => x.item_buy_num != 1)
                    .GroupBy(x => x.item_id))
                {
                    if (k == 5) k = 0;
                    var name = Database.ClimaxItem[j.First().item_id];
                    if (name.Contains("+15") ||
                        name.Contains("体力+") ||
                        (name == "苦茶" && shouldPromoteTea) ||
                        name == "BBQ" ||
                        name == "切者" ||
                        name == "哨子" ||
                        name == "60%喇叭" ||
                        name == "御守" ||
                        name == "蹄铁・極"
                        )
                        name = $"[green]{name}[/]";
                    var itemCount = j.Count();
                    var remainTurn = j.First().limit_turn == 0 ? ((currentTurn - 1) / 6 + 1) * 6 + 1 - currentTurn : j.First().limit_turn + 1 - currentTurn;
                    var remainTurnRemind = $"{remainTurn}T";
                    if (remainTurn == 3)
                        remainTurnRemind = $"[green1]{remainTurnRemind}[/]";
                    else if (remainTurn == 2)
                        remainTurnRemind = $"[orange1]{remainTurnRemind}[/]";
                    else if (remainTurn == 1)
                        remainTurnRemind = $"[red]{remainTurnRemind}[/]";
                    rows[k].Add($"{name}:{itemCount}/{remainTurnRemind}");
                    k++;
                }
                for (var i = 0; i < 5; ++i)
                {
                    table.Columns[i].Footer = new Rows(rows[i].Select(x => new Markup(x)));
                }
            }
            if (@event.IsScenario(ScenarioType.GrandMasters))
            {
                foreach (var i in @event.data.venus_data_set.venus_chara_command_info_array)
                {
                    switch (i.command_type)
                    {
                        case 1:
                            switch (i.command_id)
                            {
                                case 101 or 601:
                                    table.Columns[0].Header = new Markup($"速{failureRateStr[0]} | {GameGlobal.GrandMastersSpiritNamesColored[i.spirit_id]}{(i.is_boost == 1 ? "[aqua]x2[/]" : string.Empty)}"); break;
                                case 102 or 603:
                                    table.Columns[2].Header = new Markup($"力{failureRateStr[2]} | {GameGlobal.GrandMastersSpiritNamesColored[i.spirit_id]}{(i.is_boost == 1 ? "[aqua]x2[/]" : string.Empty)}"); break;
                                case 103 or 604:
                                    table.Columns[3].Header = new Markup($"根{failureRateStr[3]} | {GameGlobal.GrandMastersSpiritNamesColored[i.spirit_id]}{(i.is_boost == 1 ? "[aqua]x2[/]" : string.Empty)}"); break;
                                case 105 or 602:
                                    table.Columns[1].Header = new Markup($"耐{failureRateStr[1]} | {GameGlobal.GrandMastersSpiritNamesColored[i.spirit_id]}{(i.is_boost == 1 ? "[aqua]x2[/]" : string.Empty)}"); break;
                                case 106 or 605:
                                    table.Columns[4].Header = new Markup($"智{failureRateStr[4]} | {GameGlobal.GrandMastersSpiritNamesColored[i.spirit_id]}{(i.is_boost == 1 ? "[aqua]x2[/]" : string.Empty)}"); break;
                            }
                            break;
                        case 3:
                            table.Columns[0].Footer = new Rows(new Markup($"出行 | {GameGlobal.GrandMastersSpiritNamesColored[i.spirit_id]}{(i.is_boost == 1 ? "[aqua]x2[/]" : string.Empty)}"));
                            break;
                        case 4:
                            table.Columns[2].Footer = new Rows(new Markup($"比赛 | {GameGlobal.GrandMastersSpiritNamesColored[i.spirit_id]}{(i.is_boost == 1 ? "[aqua]x2[/]" : string.Empty)}"));
                            break;
                        case 7:
                            table.Columns[1].Footer = new Rows(new Markup($"休息 | {GameGlobal.GrandMastersSpiritNamesColored[i.spirit_id]}{(i.is_boost == 1 ? "[aqua]x2[/]" : string.Empty)}"));
                            break;
                    }
                }
            }
            if (@event.IsScenario(ScenarioType.LArc) && @event.data.arc_data_set.selection_info != null)
            {
                var selectedRivalCount = @event.data.arc_data_set.selection_info.selection_rival_info_array.Length;
                turnStat.larc_SSPersonCount = selectedRivalCount;
                turnStat.larc_isSSS = @event.data.arc_data_set.selection_info.is_special_match == 1;
                for (var i = 0; i < selectedRivalCount; i++)
                {
                    var rival = @event.data.arc_data_set.selection_info.selection_rival_info_array[i];
                    var rivalName = Database.Names.GetCharacter(rival.chara_id).Nickname;
                    if (selectedRivalCount == 5)
                    {
                        var sc = supportCards.Values.FirstOrDefault(sc => rival.chara_id == Database.Names.GetSupportCard(sc).CharaId); // SS Match中的S卡，值为defau时即为NPC
                        if (@event.data.arc_data_set.selection_info.selection_rival_info_array[i].mark != 1)
                            rivalName = $"[#ff0000]{rivalName}(可能失败)[/]";
                        else if (sc != default && @event.data.chara_info.evaluation_info_array[supportCards.First(x => x.Value == sc).Key - 1].evaluation < 80)
                            rivalName = $"[yellow]{rivalName}[/]"; // 羁绊不满80的S卡
                        else if (@event.data.arc_data_set.selection_info.is_special_match == 1)
                            rivalName = $"[#00ffff]{rivalName}[/]"; // SSS Match
                        else
                            rivalName = $"[#00ff00]{rivalName}[/]";
                    }

                    var arc_data = @event.data.arc_data_set.arc_rival_array.First(x => x.chara_id == rival.chara_id);
                    var effectId = arc_data.selection_peff_array.First(x => x.effect_num == arc_data.selection_peff_array.Min(x => x.effect_num)).effect_group_id;
                    rivalName += $"({GameGlobal.LArcSSEffectNameColored[effectId]})";
                    table.Edit(5, i, rivalName);
                }
                // 把攒满但没进ss的人头也显示在下面
                if (selectedRivalCount == 5)
                {
                    table.Edit(5, 5, separatorLineSSMatch);

                    var otherChargedRivals = @event.data.arc_data_set.arc_rival_array
                        .Where(rival =>
                               !(rival.selection_peff_array == null // 马娘自身
                            || rival.rival_boost != 3 // 没攒满
                            || @event.data.arc_data_set.selection_info.selection_rival_info_array.Any(x => x.chara_id == rival.chara_id)) // 已经在ss训练中了
                            );
                    if (otherChargedRivals.Any())
                    {
                        table.Edit(5, 6, "[#ffff00]其他满格人头:[/]");
                        var chargedRivalCount = 0;
                        foreach (var rival in otherChargedRivals)
                        {
                            chargedRivalCount++;
                            if (chargedRivalCount > 5) break;
                            var rivalName = Database.Names.GetCharacter(rival.chara_id).Nickname;
                            var effectId = rival.selection_peff_array.First(x => x.effect_num == rival.selection_peff_array.Min(x => x.effect_num)).effect_group_id;
                            rivalName += $"({GameGlobal.LArcSSEffectNameColored[effectId]})";
                            table.Edit(5, chargedRivalCount + 6, rivalName);
                        }

                        if (otherChargedRivals.Count() > 5)//有没显示的
                        {
                            table.Edit(5, 12, $"[#ffff00]... + {otherChargedRivals.Count() - 5} 人[/]");
                        }
                    }
                }
                // 增加当前SS训练属性和PT的显示
                if (selectedRivalCount > 0)
                {
                    var totalStats = 0;
                    var totalPt = 0;
                    var totalVital = 0;
                    if (@event.data.arc_data_set.selection_info.params_inc_dec_info_array != null)
                    {
                        totalStats += @event.data.arc_data_set.selection_info.params_inc_dec_info_array
                            .Where(x => x.target_type >= 1 && x.target_type <= 5)
                            .Sum(x => x.value);
                        totalPt += @event.data.arc_data_set.selection_info.params_inc_dec_info_array
                            .Where(x => x.target_type == 30)
                            .Sum(x => x.value);
                        totalVital += @event.data.arc_data_set.selection_info.params_inc_dec_info_array
                            .Where(x => x.target_type == 10)
                            .Sum(x => x.value);
                    }
                    if (@event.data.arc_data_set.selection_info.bonus_params_inc_dec_info_array != null)
                    {
                        totalStats += @event.data.arc_data_set.selection_info.bonus_params_inc_dec_info_array
                            .Where(x => x.target_type >= 1 && x.target_type <= 5)
                            .Sum(x => x.value);
                        totalPt += @event.data.arc_data_set.selection_info.bonus_params_inc_dec_info_array
                            .Where(x => x.target_type == 30)
                            .Sum(x => x.value);
                        totalVital += @event.data.arc_data_set.selection_info.bonus_params_inc_dec_info_array
                            .Where(x => x.target_type == 10)
                            .Sum(x => x.value);
                    }
                    table.Edit(5, 13, $"[#00ffff]属性:{totalStats}|Pt:{totalPt}[/]");
                }
            }
            table.Finish();
            AnsiConsole.Write(table);

            //远征/没买友情+20%或者pt+10警告
            if (@event.IsScenario(ScenarioType.LArc))
            {
                //两次远征分别是37,60回合
                if (turnNum >= 34 && turnNum < 37)
                    AnsiConsole.MarkupLine($@"[#ff0000]还有{37 - turnNum}回合第二年远征！[/]");
                else if (turnNum >= 55 && turnNum < 60)
                    AnsiConsole.MarkupLine($@"[#ff0000]还有{60 - turnNum}回合第三年远征！[/]");
                if (turnNum == 59)
                {
                    AnsiConsole.MarkupLine($@"[#00ffff]下回合第三年远征！[/]");
                    AnsiConsole.MarkupLine($@"[#00ffff]下回合第三年远征！[/]");
                    AnsiConsole.MarkupLine($@"[#00ffff]下回合第三年远征！（重要的事情说三遍）[/]");
                }

                if (turnNum > 42)
                {
                    //十个升级的id分别是
                    //  2 5
                    // 1 4 6
                    // 3 7 8
                    //  9 10
                    //检查是否买了友情+20
                    var friendLevel = @event.data.arc_data_set.arc_info.potential_array.First(x => x.potential_id == 8).level;
                    var ptLevel = @event.data.arc_data_set.arc_info.potential_array.First(x => x.potential_id == 3).level;
                    if (friendLevel < 3)//没买友情
                    {
                        var cost = friendLevel == 2 ? 300 : 500;
                        if (@event.data.arc_data_set.arc_info.global_exp >= cost)
                        {
                            AnsiConsole.MarkupLine($@"[#00ffff]没买友情+20%！[/]");
                            AnsiConsole.MarkupLine($@"[#00ffff]没买友情+20%！[/]");
                            AnsiConsole.MarkupLine($@"[#00ffff]没买友情+20%！（重要的事情说三遍）[/]");
                        }
                    }
                    else if (ptLevel < 3)//买了友情但没买pt+10
                    {
                        var cost = ptLevel == 2 ? 200 : 400;
                        if (@event.data.arc_data_set.arc_info.global_exp >= cost)
                        {
                            AnsiConsole.MarkupLine($@"[#00ffff]没买pt+10！[/]");
                            AnsiConsole.MarkupLine($@"[#00ffff]没买pt+10！[/]");
                            AnsiConsole.MarkupLine($@"[#00ffff]没买pt+10！（重要的事情说三遍）[/]");
                        }
                    }

                    // 大逃日本杯提示
                    if (turnNum == 46)
                        AnsiConsole.MarkupLine($@"[#ffff00]日本杯！拿大逃别忘了打！[/]");
                }
            }
            //发送AI所需信息
            if (@event.IsScenario(ScenarioType.UAF))
            {
                try
                {
                    var gameStatusToSend = new GameStatusSend_UAF(@event);
                    SubscribeAiInfo.Signal(gameStatusToSend);
                    AnsiConsole.MarkupLine("[aqua]AI计算中...[/]");
                    if (Config.Get(Localization.Config.I18N_WriteAIInfo))
                    {
                        var currentGSdirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "GameData");
                        Directory.CreateDirectory(currentGSdirectory);

                        var success = false;
                        var tried = 0;
                        do
                        {
                            try
                            {
                                var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }; // 去掉空值避免C++端抽风
                                File.WriteAllText($@"{currentGSdirectory}/thisTurn.json", JsonConvert.SerializeObject(gameStatusToSend, Formatting.Indented, settings));
                                File.WriteAllText($@"{currentGSdirectory}/turn{@event.data.chara_info.turn}.json", JsonConvert.SerializeObject(gameStatusToSend, Formatting.Indented, settings));
                                success = true; // 写入成功，跳出循环
                                break;
                            }
                            catch
                            {
                                tried++;
                                AnsiConsole.MarkupLine("[yellow]写入失败，0.5秒后重试...[/]");
                                await Task.Delay(500); // 等待0.5秒
                            }
                        } while (!success && tried < 10);
                        if (!success)
                        {
                            AnsiConsole.MarkupLine($@"[red]写入{currentGSdirectory}/thisTurn.json失败！[/]");
                        }
                    }
                }
                catch (Exception e)
                {
                    AnsiConsole.MarkupLine($"[red]向AI发送数据失败！错误信息：{Environment.NewLine}{e.Message}[/]");
                }
            } // if
        }
    }
}
