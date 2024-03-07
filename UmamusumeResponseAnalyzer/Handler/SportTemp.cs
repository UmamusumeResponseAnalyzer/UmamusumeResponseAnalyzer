using Gallop;
using MathNet.Numerics.Distributions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.AI;
using UmamusumeResponseAnalyzer.Communications.Subscriptions;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using static UmamusumeResponseAnalyzer.Game.TurnInfo.TurnInfoUAF;

using System.Text.RegularExpressions;

using MessagePack;

using System;
using System.ComponentModel.Design;
using System.IO.Pipes;
using System.Linq;
using System.Xml.Linq;

using System.Security.Cryptography;


namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseSportCommandInfo(Gallop.SingleModeCheckEventResponse @event)
        {
            if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0) || @event.data.race_start_info != null) return;
            var layout = new Layout().SplitColumns(
                new Layout("Main").SplitRows(
                    new Layout("体力干劲条").SplitColumns(
                        new Layout("日期").Ratio(4),
                        new Layout("赛程倒计时").Ratio(3),
                        new Layout("体力").Ratio(9),
                        new Layout("干劲").Ratio(3)).Size(3),
                    new Layout("重要信息").Size(5),
                    new Layout("分割", new Rule()).Size(1),
                    new Layout("训练信息")
                    ).Ratio(4),
                new Layout("Ext").Ratio(1)
                );
            var extInfos = new List<string>();
            var critInfos = new List<string>();
            var turn = new TurnInfoUAF(@event.data);

            if (GameStats.currentTurn != turn.Turn - 1 //正常情况
                && GameStats.currentTurn != turn.Turn //重复显示
                && turn.Turn != 1 //第一个回合
                )
            {
                GameStats.isFullGame = false;
                critInfos.Add($"[red]警告：回合数不正确，上一个回合为{GameStats.currentTurn}，当前回合为{turn.Turn}[/]");
                EventLogger.Init();
            }
            else if (turn.Turn == 1)
            {
                GameStats.isFullGame = true;
                EventLogger.Init();
            }

            //买技能，大师杯剧本年末比赛，会重复显示
            if (@event.data.chara_info.playing_state != 1)
            {
                critInfos.Add($"[yellow]******此回合为重复显示******[/]");
            }
            else
            {
                //初始化TurnStats
                GameStats.whichScenario = @event.data.chara_info.scenario_id;
                GameStats.currentTurn = turn.Turn;
                GameStats.stats[turn.Turn] = new TurnStats();
                EventLogger.Update(@event);
            }
            var blueFever = false;
            var redFever = false;
            var yellowFever = false;

            var trainItems = new Dictionary<int, SingleModeCommandInfo>
            {
                { 101, @event.data.home_info.command_info_array[0] },
                { 105, @event.data.home_info.command_info_array[1] },
                { 102, @event.data.home_info.command_info_array[2] },
                { 103, @event.data.home_info.command_info_array[3] },
                { 106, @event.data.home_info.command_info_array[4] }
            };
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
                foreach (var item in turn.GetCommonResponse().home_info.command_info_array)
                    if (GameGlobal.ToTrainId.TryGetValue(item.command_id, out int value) && value == trainId)
                        foreach (var trainParam in item.params_inc_dec_info_array)
                            trainParams[trainParam.target_type] += trainParam.value;
                foreach (var item in turn.GetCommonResponse().sport_data_set.command_info_array)
                    if (GameGlobal.ToTrainId.TryGetValue(item.command_id, out int value) && value == trainId)
                        foreach (var trainParam in item.params_inc_dec_info_array)
                            trainParams[trainParam.target_type] += trainParam.value;

                var stats = new TrainStats
                {
                    FailureRate = trainItems[trainId].failure_rate,
                    VitalGain = trainParams[10]
                };
                if (turn.Vital + stats.VitalGain > turn.MaxVital)
                    stats.VitalGain = turn.MaxVital - turn.Vital;
                if (stats.VitalGain < -turn.Vital)
                    stats.VitalGain = -turn.Vital;
                stats.FiveValueGain = [trainParams[1], trainParams[2], trainParams[3], trainParams[4], trainParams[5]];
                for (var j = 0; j < 5; j++)
                    stats.FiveValueGain[j] = ScoreUtils.ReviseOver1200(turn.Stats[j] + stats.FiveValueGain[j]) - ScoreUtils.ReviseOver1200(turn.Stats[j]);
                stats.PtGain = trainParams[30];
                trainStats[i] = stats;
            }

            var grids = new Grid();
            grids.AddColumns(5);
            var maxColor = turn.CommandInfoArray.GroupBy(x => x.Color).MaxBy(x => x.Count())?.Key;

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
            var 友人在的训练 = turn.CommandInfoArray.FirstOrDefault(x => x.TrainingPartners.Any(y => y.CardId == 30188 || y.CardId == 10104));
            if (友人在的训练 != default)
            {
                GameStats.stats[turn.Turn].uaf_friendAtTrain[友人在的训练.TrainIndex - 1] = true;
            }
            var commands = turn.CommandInfoArray.Select(command =>
            {
                var table = new Table()
                .AddColumn(command.TrainIndex switch
                {
                    1 => $"速{failureRateStr[0]}",
                    2 => $"耐{failureRateStr[1]}",
                    3 => $"力{failureRateStr[2]}",
                    4 => $"根{failureRateStr[3]}",
                    5 => $"智{failureRateStr[4]}"
                })
                .BorderColor(command.Color switch
                {
                    SportColor.Blue => Color.Blue,
                    SportColor.Red => Color.Red,
                    SportColor.Yellow => Color.Yellow,
                    _ => throw new NotImplementedException(),
                });

                var currentStat = turn.StatsRevised[command.TrainIndex - 1];
                var statUpToMax = turn.MaxStatsRevised[command.TrainIndex - 1] - currentStat;
                table.AddRow("当前:可获得");
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
                    < 30 => $"体力:[red]{afterVital}[/]/{turn.MaxVital}",
                    < 50 => $"体力:[darkorange]{afterVital}[/]/{turn.MaxVital}",
                    < 70 => $"体力:[yellow]{afterVital}[/]/{turn.MaxVital}",
                    _ => $"体力:[green]{afterVital}[/]/{turn.MaxVital}"
                });
                table.AddRow($"Lv{command.TrainLevel} | Sp{command.SportRank}");
                table.AddRow(new Rule());

                var stats = trainStats[command.TrainIndex - 1];
                var score = stats.FiveValueGain.Sum();
                if (score == trainStats.Max(x => x.FiveValueGain.Sum()))
                    table.AddRow($"属:[aqua]{score}[/]|Pt:{stats.PtGain}");
                else
                    table.AddRow($"属:{score}|Pt:{stats.PtGain}");

                blueFever = (turn.BlueLevel % 50 + command.TotalGainRank) >= 50;
                redFever = (turn.RedLevel % 50 + command.TotalGainRank) >= 50;
                yellowFever = (turn.YellowLevel % 50 + command.TotalGainRank) >= 50;
                switch (command.Color)
                {
                    case SportColor.Blue:
                        table.AddRow(blueFever ? $"[blue]获得Rank:{command.TotalGainRank}[/]" : $"获得Rank:{command.TotalGainRank}");
                        break;
                    case SportColor.Red:
                        table.AddRow(redFever ? $"[red]获得Rank:{command.TotalGainRank}[/]" : $"获得Rank:{command.TotalGainRank}");
                        break;
                    case SportColor.Yellow:
                        table.AddRow(yellowFever ? $"[yellow]获得Rank:{command.TotalGainRank}[/]" : $"获得Rank:{command.TotalGainRank}");
                        break;
                }
                table.AddRow(new Rule());

                foreach (var trainingPartner in command.TrainingPartners)
                {
                    table.AddRow(trainingPartner.Name);
                }
                for (var i = 5 - command.TrainingPartners.Count(); i > 0; i--)
                {
                    table.AddRow(string.Empty);
                }

                return new Padder(table).Padding(0, command.Color == maxColor ? 0 : 1, 0, 0);
            });
            grids.AddRow([.. commands]);
            layout["训练信息"].Update(grids);
            if (turn.IsRankGainIncreased)
                critInfos.Add("❗当前有项目等级加成");

            if (turn.AvailableTalkCount > 0)
            {
                if (turn.Turn % 12 >= 9)
                    critInfos.Add("❗请及时使用相谈");
                // 每种颜色按最大获得Rank排序
                var groupByColorOrderByRank = turn.CommandInfoArray
                    .GroupBy(x => x.Color)
                    .OrderByDescending(x => x.Sum(y => y.GainRank));
                foreach (var i in groupByColorOrderByRank.Where(x => x.Key != SportColor.Blue)) // Color,ParsedCommandInfo
                {
                    var totalRank = turn.CommandInfoArray.Where(x => x.Color == SportColor.Blue).FirstOrDefault()?.TotalGainRank ?? 0;
                    foreach (var j in i)
                    {
                        var changed = turn.TrainingArray.First(x => x.CommandId == int.Parse($"210{j.CommandId % 10}"));
                        if (changed.SportRank + j.ActualGainRank >= 100)
                            totalRank += 100 - changed.SportRank;
                        else
                            totalRank += j.ActualGainRank;
                    }
                    if ((turn.BlueLevel % 50) + totalRank >= 50)
                    {
                        extInfos.Add($"✨{ColorToMarkup(i.Key)}变[blue]蓝[/]可爆");
                    }
                }
                foreach (var i in groupByColorOrderByRank.Where(x => x.Key != SportColor.Red)) // Color,ParsedCommandInfo
                {
                    var totalRank = turn.CommandInfoArray.Where(x => x.Color == SportColor.Red).FirstOrDefault()?.TotalGainRank ?? 0;
                    foreach (var j in i)
                    {
                        var changed = turn.TrainingArray.First(x => x.CommandId == int.Parse($"220{j.CommandId % 10}"));
                        if (changed.SportRank + j.ActualGainRank >= 100)
                            totalRank += 100 - changed.SportRank;
                        else
                            totalRank += j.ActualGainRank;
                    }
                    if ((turn.RedLevel % 50) + totalRank >= 50)
                    {
                        extInfos.Add($"✨{ColorToMarkup(i.Key)}变[red]红[/]可爆");
                    }
                }
                foreach (var i in groupByColorOrderByRank.Where(x => x.Key != SportColor.Yellow)) // Color,ParsedCommandInfo
                {
                    var totalRank = turn.CommandInfoArray.Where(x => x.Color == SportColor.Yellow).FirstOrDefault()?.TotalGainRank ?? 0;
                    foreach (var j in i)
                    {
                        var changed = turn.TrainingArray.First(x => x.CommandId == int.Parse($"230{j.CommandId % 10}"));
                        if (changed.SportRank + j.ActualGainRank >= 100)
                            totalRank += 100 - changed.SportRank;
                        else
                            totalRank += j.ActualGainRank;
                    }
                    if ((turn.YellowLevel % 50) + totalRank >= 50)
                    {
                        extInfos.Add($"✨{ColorToMarkup(i.Key)}变[yellow]黄[/]可爆");
                    }
                }
            }
            var nextRank = turn.Turn switch
            {
                <= 12 => 0,
                <= 24 => 10,
                <= 36 => 20,
                <= 48 => 30,
                <= 60 => 40,
                _ => 50
            };
            var lowRankSports = turn.TrainingArray.Where(x => x.SportRank < nextRank);
            if (lowRankSports.Any())
            {
                foreach (var i in lowRankSports)
                {
                    extInfos.Add($"❗{ColorToMarkup(i.Color)}色的{GameGlobal.TrainNames[GameGlobal.ToTrainId[i.CommandId]]}等级过低");
                }
                extInfos.Add($"❗当前时期的运动等级最低为{nextRank}");
                extInfos.Add(string.Empty);

                if (turn.AvailableTalkCount > 0)
                {
                    // 按照缺少等级的训练数量由多到少排列
                    var groupByColor = lowRankSports.GroupBy(x => x.Color).OrderByDescending(x => x.Count());
                    var requireRankGroup = groupByColor.FirstOrDefault();
                    if (requireRankGroup != null)
                    {
                        var requireRankColor = requireRankGroup.Key;
                        var requireSportCmd = requireRankGroup.Select(x => x.TrainIndex);
                        // 仅在有至少两种颜色都不够等级时才显示
                        if (requireRankGroup.Count() >= 2)
                        {
                            var otherColorCommands = turn.CommandInfoArray.Where(x => x.Color != requireRankColor && requireSportCmd.Contains(x.TrainIndex));
                            if (otherColorCommands.Any())
                            {
                                var maxEffective = otherColorCommands
                                    .GroupBy(x => x.Color)
                                    .OrderByDescending(x => x.Sum(y => y.GainRank))
                                    .FirstOrDefault();
                                if (maxEffective != default)
                                {
                                    var maxEffectiveRankUp = maxEffective.Sum(y => y.GainRank);
                                    // 如果被转换的颜色也有等级不够的
                                    var wastedRank = groupByColor.FirstOrDefault(x => x.Key == maxEffective.Key);
                                    // 去掉被浪费的，实际因相谈而提升了等级的总数
                                    var actualEffectiveRank = wastedRank == default ? maxEffectiveRankUp : maxEffective.Where(x => !wastedRank.Any(y => y.TrainIndex == x.TrainIndex)).Sum(x => x.GainRank);
                                    if (maxEffectiveRankUp == actualEffectiveRank)
                                        extInfos.Add($"❗{ColorToMarkup(maxEffective.Key)}转换为{ColorToMarkup(requireRankColor)}为最大等级提升{maxEffective.Sum(y => y.GainRank)}");
                                    else
                                        extInfos.Add($"❗{ColorToMarkup(maxEffective.Key)}转换为{ColorToMarkup(requireRankColor)}为最大等级提升{maxEffective.Sum(y => y.GainRank)}{Environment.NewLine}实际有效提升为{actualEffectiveRank}");
                                }
                            }
                        }
                    }
                    var currentCommandIds = turn.CommandInfoArray.Select(x => x.CommandId).Where(x => lowRankSports.Contains(y => y.CommandId == x));
                    var availableSportTrains = turn.CommandInfoArray.Where(x => currentCommandIds.Contains(x.CommandId));
                    var maxEffectiveWithoutTalk = availableSportTrains.GroupBy(x => x.Color).OrderByDescending(x => x.Sum(y => y.GainRank)).FirstOrDefault();
                    if (maxEffectiveWithoutTalk != default)
                        extInfos.Add($"❗不相谈的情况下{ColorToMarkup(maxEffectiveWithoutTalk.Key)}为最大等级提升{maxEffectiveWithoutTalk.Sum(x => x.GainRank)}");
                    extInfos.Add(string.Empty);
                }
            }

            //友人点了几次，来了几次
            var friendClickedTimes = 0;
            var friendChargedTimes = 0; //友人冲了几次体力
            for (var t = turn.Turn; t >= 1; t--)
            {
                if (GameStats.stats[t] == null) break;
                if (!GameGlobal.TrainIds.Any(x => x == GameStats.stats[t].playerChoice)) //没训练
                    continue;
                if (GameStats.stats[t].isTrainingFailed)//训练失败
                    continue;
                if (!GameStats.stats[t].uaf_friendAtTrain[GameGlobal.ToTrainIndex[GameStats.stats[t].playerChoice]])
                    continue;//没点友人
                if (GameStats.stats[t].uaf_friendEvent == 5)//启动事件
                    continue;//没点佐岳
                friendClickedTimes += 1;
                if (GameStats.stats[t].uaf_friendEvent == 1 || GameStats.stats[t].uaf_friendEvent == 2)
                    friendChargedTimes += 1;
            }
            extInfos.Add($"共点了[aqua]{friendClickedTimes}[/]次凉花");
            extInfos.Add($"加了[aqua]{friendChargedTimes}[/]次体力");
            // 计算佐岳表现（分位数）
            var friendPerformance = string.Empty;
            if (friendClickedTimes > 1)
            {
                // (p(n<=k-1) + p(n<=k)) / 2
                double bn = Binomial.CDF(0.4, friendClickedTimes, friendChargedTimes);
                double bn_1 = Binomial.CDF(0.4, friendClickedTimes, friendChargedTimes - 1);
                friendPerformance = $"，超过了[aqua]{(bn + bn_1) / 2 * 100:0}%[/]的凉花";
            }
            extInfos.Add(friendPerformance);

            layout["日期"].Update(new Panel($"{turn.Year}年 {turn.Month}月{turn.HalfMonth}").Expand());
            layout["赛程倒计时"].Update(new Panel("占位-占位").Expand());
            layout["体力"].Update(new Panel($"体力: [green]{turn.Vital}[/]/{turn.MaxVital}").Expand());
            layout["干劲"].Update(new Panel(@event.data.chara_info.motivation switch
            {
                5 => "[green]绝好调↑[/]",
                4 => "[yellow]好调↗[/]",
                3 => "[red]普通→[/]",
                2 => "[red]不调↘️[/]",
                1 => "[red]绝不调↓[/]"
            }).Expand());
            layout["重要信息"].Update(new Panel(string.Join(Environment.NewLine, critInfos)).Expand());
            layout["Ext"].Update(new Panel(string.Join(Environment.NewLine, extInfos)));
            AnsiConsole.Write(layout);

            static string ColorToMarkup(SportColor c) =>
                c switch
                {
                    SportColor.Blue => $"[blue]蓝[/]",
                    SportColor.Red => $"[red]红[/]",
                    SportColor.Yellow => $"[yellow]黄[/]",
                    _ => throw new NotImplementedException(),
                };


            if (@event.IsScenario(ScenarioType.UAF))
            {
                try
                {
                    var gameStatusToSend = new GameStatusSend_UAF(@event);
                    SubscribeAiInfo.Signal(gameStatusToSend);

                    if (Config.Get(Localization.Resource.ConfigSet_WriteAIInfo))
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
                                //await Task.Delay(500); // 等待0.5秒
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
