using Gallop;
using MathNet.Numerics.Distributions;
using Newtonsoft.Json;
using Spectre.Console;
using System.Text;
using UmamusumeResponseAnalyzer.AI;
using UmamusumeResponseAnalyzer.Communications.Subscriptions;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using UmamusumeResponseAnalyzer.LocalizedLayout.Handlers;
using static UmamusumeResponseAnalyzer.Localization.CommandInfo.Cook;
using static UmamusumeResponseAnalyzer.Localization.CommandInfo.UAF;
using static UmamusumeResponseAnalyzer.Localization.Game;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseMechaCommandInfo(Gallop.SingleModeCheckEventResponse @event)
        {
            if (@event.data.chara_info.playing_state != 26 && ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0) || @event.data.race_start_info != null)) return;

            var layout = new Layout().SplitColumns(
                new Layout("Main").Size(CommandInfoLayout.Current.MainSectionWidth).SplitRows(
                    new Layout("体力干劲条").SplitColumns(
                        new Layout("日期").Ratio(4),
                        new Layout("总属性").Ratio(6),
                        new Layout("体力").Ratio(6),
                        new Layout("干劲").Ratio(3)).Size(3),
                    new Layout("剧本属性").SplitColumns(
                        new Layout("总研究Lv").Ratio(3),
                        new Layout("总EN").Ratio(3),
                        new Layout("EN分配").Ratio(3),
                        new Layout("齿轮槽").Ratio(3)
                        ).Size(3),
                    new Layout("重要信息").Size(5),
                    //new Layout("分割", new Rule()).Size(1),
                    new Layout("训练信息")  // size 20, 共约30行
                    ).Ratio(4),
                new Layout("Ext").Ratio(1)
                );
            var extInfos = new List<string>();
            var critInfos = new List<string>();
            var turn = new TurnInfoMecha(@event.data);
            var dataset = @event.data.mecha_data_set;

            if (GameStats.currentTurn != turn.Turn - 1 //正常情况
                && GameStats.currentTurn != turn.Turn //重复显示
                && turn.Turn != 1 //第一个回合
                )
            {
                GameStats.isFullGame = false;
                critInfos.Add(string.Format(I18N_WrongTurnAlert, GameStats.currentTurn, turn.Turn));
                EventLogger.Init(@event);
            }
            else if (turn.Turn == 1)
            {
                GameStats.isFullGame = true;
                EventLogger.Init(@event);
            }

            var isRepeat = @event.data.chara_info.playing_state != 1;
            //买技能，大师杯剧本年末比赛，会重复显示
            if (isRepeat)
            {
                critInfos.Add(I18N_RepeatTurn);
            }
            else
            {
                //初始化TurnStats
                GameStats.whichScenario = @event.data.chara_info.scenario_id;
                GameStats.currentTurn = turn.Turn;
                GameStats.stats[turn.Turn] = new TurnStats();
                EventLogger.Update(@event);
            }
            var turnStat = isRepeat ? new TurnStats() : GameStats.stats[turn.Turn];

            var trainItems = new Dictionary<int, SingleModeCommandInfo>
            {
                { GameGlobal.TrainIdsMecha[0], @event.data.home_info.command_info_array[0] },
                { GameGlobal.TrainIdsMecha[1], @event.data.home_info.command_info_array[1] },
                { GameGlobal.TrainIdsMecha[2], @event.data.home_info.command_info_array[2] },
                { GameGlobal.TrainIdsMecha[3], @event.data.home_info.command_info_array[3] },
                { GameGlobal.TrainIdsMecha[4], @event.data.home_info.command_info_array[4] }
            };
            var trainStats = new TrainStats[5];
            var failureRate = new Dictionary<int, int>();

            // 总属性计算
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
            var currentFiveValueRevised = currentFiveValue.Select(ScoreUtils.ReviseOver1200).ToArray();
            var totalValue = currentFiveValueRevised.Sum();
            var totalValueWithPt = totalValue + @event.data.chara_info.skill_point;

            for (var i = 0; i < 5; i++)
            {
                var trainId = GameGlobal.TrainIdsMecha[i];
                var trainItem = trainItems.ElementAt(i);
                failureRate[i] = trainItem.Value.failure_rate;
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
                //foreach (var item in turn.GetCommonResponse().home_info.command_info_array)
                //{
                //    if (GameGlobal.ToTrainId.TryGetValue(item.command_id, out var value) && value == trainId)
                //    {
                //        foreach (var trainParam in item.params_inc_dec_info_array)
                //            trainParams[trainParam.target_type] += trainParam.value;
                //    }
                //}
                foreach (var trainParam in trainItem.Value.params_inc_dec_info_array)
                    trainParams[trainParam.target_type] += trainParam.value;

                var stats = new TrainStats
                {
                    FailureRate = trainItem.Value.failure_rate,
                    VitalGain = trainParams[10]
                };
                if (turn.Vital + stats.VitalGain > turn.MaxVital)
                    stats.VitalGain = turn.MaxVital - turn.Vital;
                if (stats.VitalGain < -turn.Vital)
                    stats.VitalGain = -turn.Vital;
                stats.FiveValueGain = [trainParams[1], trainParams[2], trainParams[3], trainParams[4], trainParams[5]];
                stats.PtGain = trainParams[30];

                // 取上半数值
                var gainUpper = dataset.command_info_array.FirstOrDefault(x => x.command_id == trainItem.Value.command_id || x.command_id == GameGlobal.XiahesuIds[GameGlobal.ToTrainId[trainId]])?.params_inc_dec_info_array;
                if (gainUpper != null)
                {
                    foreach (var item in gainUpper)
                    {
                        if (item.target_type == 30)
                            stats.PtGain += item.value;
                        else if (item.target_type <= 5)
                            stats.FiveValueGain[item.target_type - 1] += item.value;
                        else
                            AnsiConsole.MarkupLine("[red]here[/]");
                    }
                }

                for (var j = 0; j < 5; j++)
                    stats.FiveValueGain[j] = ScoreUtils.ReviseOver1200(turn.Stats[j] + stats.FiveValueGain[j]) - ScoreUtils.ReviseOver1200(turn.Stats[j]);

                if (turn.Turn == 1)
                {
                    turnStat.trainLevel[i] = 1;
                    turnStat.trainLevelCount[i] = 0;
                }
                else
                {
                    var lastTrainLevel = GameStats.stats[turn.Turn - 1] != null ? GameStats.stats[turn.Turn - 1].trainLevel[i] : 1;
                    var lastTrainLevelCount = GameStats.stats[turn.Turn - 1] != null ? GameStats.stats[turn.Turn - 1].trainLevelCount[i] : 0;

                    turnStat.trainLevel[i] = lastTrainLevel;
                    turnStat.trainLevelCount[i] = lastTrainLevelCount;
                    if (GameStats.stats[turn.Turn - 1] != null &&
                        GameStats.stats[turn.Turn - 1].playerChoice == GameGlobal.TrainIds[i] &&
                        !GameStats.stats[turn.Turn - 1].isTrainingFailed &&
                        !((turn.Turn - 1 >= 37 && turn.Turn - 1 <= 40) || (turn.Turn - 1 >= 61 && turn.Turn - 1 <= 64))
                        )//上回合点的这个训练，计数+1
                        turnStat.trainLevelCount[i] += 1;
                    if (turnStat.trainLevelCount[i] >= 4)
                    {
                        turnStat.trainLevelCount[i] -= 4;
                        turnStat.trainLevel[i] += 1;
                    }
                    //检查是否有UAE
                    if (turn.Turn==25|| turn.Turn == 49|| turn.Turn==73)
                        turnStat.trainLevelCount[i] += 4;
                    if (turnStat.trainLevelCount[i] >= 4)
                    {
                        turnStat.trainLevelCount[i] -= 4;
                        turnStat.trainLevel[i] += 1;
                    }

                    if (turnStat.trainLevel[i] >= 5)
                    {
                        turnStat.trainLevel[i] = 5;
                        turnStat.trainLevelCount[i] = 0;
                    }

                    var trainlv = @event.data.chara_info.training_level_info_array.First(x => x.command_id == GameGlobal.TrainIdsMecha[i]).level;
                    if (turnStat.trainLevel[i] != trainlv && !isRepeat)
                    {
                        //可能是半途开启小黑板，也可能是有未知bug
                        critInfos.Add($"[red]警告：训练等级预测错误，预测{GameGlobal.TrainNames[GameGlobal.TrainIds[i]]}为lv{turnStat.trainLevel[i]}(+{turnStat.trainLevelCount[i]})，实际为lv{trainlv}[/]");
                        turnStat.trainLevel[i] = trainlv;
                        turnStat.trainLevelCount[i] = 0;//如果是半途开启小黑板，则会在下一次升级时变成正确的计数
                    }
                }
                trainStats[i] = stats;
                // 把训练等级信息更新到GameStats
                GameStats.stats[turn.Turn] = turnStat;
            }

            var grids = new Grid();
            grids.AddColumns(5);

            var failureRateStr = new string[5];
            //失败率>=40%标红、>=20%(有可能大失败)标DarkOrange、>0%标黄
            for (var i = 0; i < 5; i++)
            {
                var thisFailureRate = failureRate[i];
                failureRateStr[i] = thisFailureRate switch
                {
                    >= 40 => $"[red]({thisFailureRate}%)[/]",
                    >= 20 => $"[darkorange]({thisFailureRate}%)[/]",
                    > 0 => $"[yellow]({thisFailureRate}%)[/]",
                    _ => string.Empty
                };
            }
            var commands = turn.CommandInfoArray.Select(command =>
            {
                var table = new Table()
                .AddColumn(command.TrainIndex switch
                {
                    1 => $"{I18N_Speed}{failureRateStr[0]}",
                    2 => $"{I18N_Stamina}{failureRateStr[1]}",
                    3 => $"{I18N_Power}{failureRateStr[2]}",
                    4 => $"{I18N_Nuts}{failureRateStr[3]}",
                    5 => $"{I18N_Wiz}{failureRateStr[4]}"
                });

                var currentStat = turn.StatsRevised[command.TrainIndex - 1];
                var statUpToMax = turn.MaxStatsRevised[command.TrainIndex - 1] - currentStat;
                table.AddRow(I18N_CurrentRemainStat);
                table.AddRow($"{currentStat}:{statUpToMax switch
                {
                    > 400 => $"{statUpToMax}",
                    > 200 => $"[yellow]{statUpToMax}[/]",
                    _ => $"[red]{statUpToMax}[/]"
                }}");
                table.AddRow(new Rule());

                var afterVital = trainStats[command.TrainIndex - 1].VitalGain + turn.Vital;
                table.AddRow(afterVital switch
                {
                    < 30 => $"{I18N_Vital}:[red]{afterVital}[/]/{turn.MaxVital}",
                    < 50 => $"{I18N_Vital}:[darkorange]{afterVital}[/]/{turn.MaxVital}",
                    < 70 => $"{I18N_Vital}:[yellow]{afterVital}[/]/{turn.MaxVital}",
                    _ => $"{I18N_Vital}:[green]{afterVital}[/]/{turn.MaxVital}"
                });
                if((turn.Turn >= 37 && turn.Turn <= 40) || (turn.Turn >= 61 && turn.Turn <= 64)) //夏合宿
                {
                    table.AddRow($"Lv5(夏合宿)");
                }
                else { 
                    var trlv = turnStat.trainLevel[command.TrainIndex - 1];
                    var trlvcount = turnStat.trainLevelCount[command.TrainIndex - 1];
                    if (!isRepeat && (trlv != command.TrainLevel)) //即使不一致，应该已经在前面同步过了
                        throw new Exception("训练等级不一致");
                    table.AddRow($"Lv{trlv}" + (trlv >= 5 ? "" : $" (+{trlvcount})"));
                }

                table.AddRow(new Rule());

                var stats = trainStats[command.TrainIndex - 1];
                var score = stats.FiveValueGain.Sum();
                if (score == trainStats.Max(x => x.FiveValueGain.Sum()))
                    table.AddRow($"{I18N_StatSimple}:[aqua]{score}[/]|Pt:{stats.PtGain}");
                else
                    table.AddRow($"{I18N_StatSimple}:{score}|Pt:{stats.PtGain}");

                var rival_info = @event.data.mecha_data_set.rival_info;
                var mechaLv = command.TrainIndex switch
                {
                    1 => rival_info.speed,
                    2 => rival_info.stamina,
                    3 => rival_info.power,
                    4 => rival_info.guts,
                    5 => rival_info.wiz
                };
                var mechaLvLimit = command.TrainIndex switch
                {
                    1 => rival_info.speed_limit,
                    2 => rival_info.stamina_limit,
                    3 => rival_info.power_limit,
                    4 => rival_info.guts_limit,
                    5 => rival_info.wiz_limit
                };
                var remainLv = mechaLvLimit - mechaLv;
                var lvStr =
                    remainLv > 100 ? $"[green]{mechaLv}[/]" :
                    remainLv > 25 ? $"[orange1]{mechaLv}[/]" :
                    remainLv > 0 ? $"[red]{mechaLv}[/]" :
                    remainLv == 0 ? $"[red]MAX[/]" :
                    "ERR{mechaLv}";

                // 计算颜色码：is_od*4 + is_shining*2 + is_gear
                Color[] borderColors = [Color.Grey35, Color.RoyalBlue1, Color.Green, Color.Lime, Color.DeepSkyBlue2, Color.Cyan1, Color.GreenYellow, Color.Gold1];
                var isShining = 0;
                foreach (var trainingPartner in command.TrainingPartners)
                {
                    table.AddRow(trainingPartner.Name);
                    if (trainingPartner.Shining)
                        isShining = 2;
                }
                var isGear = dataset.command_info_array.First(x => x.command_id == command.CommandId).is_recommend ? 1 : 0;
                var isOD = @event.data.mecha_data_set.overdrive_info.over_drive_state > 0 ? 4 : 0;
                table.BorderColor(borderColors[isShining + isGear + isOD]);

                for (var i = 5 - command.TrainingPartners.Count(); i > 0; i--)
                {
                    table.AddRow(string.Empty);
                }
                table.AddRow(new Rule());
                //#warning 还没做超过上限的检测 //收到的数据已经考虑上限了
                var pointUp = command.PointUpInfoArray.Sum(x => x.Value);
                // 如果等级上升量小于比赛(All+7)则标黄 <10则标红
                var pointUpColor = (turn.Turn > 12 && pointUp < 35) ? "[orange1]" : "[lime]";
                if (pointUp < 10)
                    pointUpColor = "[red]";
                table.AddRow($"{lvStr}/{mechaLvLimit} (+{pointUpColor}{pointUp}[/])");
                if (isGear > 0)
                    table.AddRow($"[royalblue1]齿[/]");
                else
                    table.AddRow("");

                return new Padder(table).Padding(0, 0, 0, 0);
            }); // foreach command
            grids.AddRow([.. commands]);
            layout["训练信息"].Update(grids);

            // 额外信息
            var exTable = new Table().AddColumn("Extras");
            exTable.HideHeaders();

            layout["日期"].Update(new Panel($"{turn.Year}{I18N_Year} {turn.Month}{I18N_Month}{turn.HalfMonth}").Expand());
            layout["总属性"].Update(new Panel($"总属性: [cyan]{totalValue}[/]  Pt: [cyan]{@event.data.chara_info.skill_point}[/]").Expand());
            layout["体力"].Update(new Panel($"{I18N_Vital}: [green]{turn.Vital}[/]/{turn.MaxVital}").Expand());
            layout["干劲"].Update(new Panel(@event.data.chara_info.motivation switch
            {
                // 换行分裂和箭头符号有关，去掉
                5 => $"[green]{I18N_MotivationBest}[/]",
                4 => $"[yellow]{I18N_MotivationGood}[/]",
                3 => $"[red]{I18N_MotivationNormal}[/]",
                2 => $"[red]{I18N_MotivationBad}[/]",
                1 => $"[red]{I18N_MotivationWorst}[/]"
            }).Expand());
            var rival_info = @event.data.mecha_data_set.rival_info;
            var mechaLvTotal = rival_info.speed + rival_info.stamina + rival_info.power + rival_info.guts + rival_info.wiz;
            var progress_rate = rival_info.progress_rate;
            layout["总研究Lv"].Update(new Panel($"总Lv:{mechaLvTotal} ({progress_rate}%)").Expand());
            layout["总EN"].Update(new Panel($"总EN:{@event.data.mecha_data_set.board_info_array.Sum(x => x.chip_info_array.First(x => x.chip_id > 2000).point) + @event.data.mecha_data_set.tuning_point}").Expand());
            layout["EN分配"].Update(new Panel(
                $"头[yellow]{@event.data.mecha_data_set.board_info_array.First(x => x.board_id == 1).chip_info_array.First(x => x.chip_id > 2000).point}[/] "+
                $"胸[yellow]{@event.data.mecha_data_set.board_info_array.First(x => x.board_id == 2).chip_info_array.First(x => x.chip_id > 2000).point}[/] "+
                $"脚[yellow]{@event.data.mecha_data_set.board_info_array.First(x => x.board_id == 3).chip_info_array.First(x => x.chip_id > 2000).point}[/] "
            ).Expand());

            var overrideRemain = @event.data.mecha_data_set.overdrive_info.remain_num;
            layout["齿轮槽"].Update(new Panel(overrideRemain switch
            {
                0 => $"齿轮：{overrideRemain}(+{@event.data.mecha_data_set.overdrive_info.energy_num})",
                1 => $"[green]齿轮：{overrideRemain}(+{@event.data.mecha_data_set.overdrive_info.energy_num})[/]",
                2 => $"[yellow]齿轮：{overrideRemain}[/]",
            }
            + (@event.data.mecha_data_set.overdrive_info.over_drive_state > 0? " [cyan]已启动[/]" :"")
            ).Expand());

            // 计算连续事件表现
            var eventPerf = EventLogger.PrintCardEventPerf(@event.data.chara_info.scenario_id);
            if (eventPerf.Count > 0)
            {
                exTable.AddRow(new Rule());
                foreach (var row in eventPerf)
                    exTable.AddRow(new Markup(row));
            }

            layout["重要信息"].Update(new Panel(string.Join(Environment.NewLine, critInfos)).Expand());

            layout["Ext"].Update(exTable);
            AnsiConsole.Write(layout);
            // 光标倒转一点
            AnsiConsole.Cursor.SetPosition(0, 31);

            if (@event.IsScenario(ScenarioType.Mecha))
            {
                var gameStatusToSend = new GameStatusSend_Mecha(@event);
                if (gameStatusToSend.islegal == false)
                {
                    return;
                }
                gameStatusToSend.doSend();
            } // if
        }
    }
}
