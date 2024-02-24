using Gallop;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseSportCommandInfo(Gallop.SingleModeCheckEventResponse @event)
        {
            if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0) || @event.data.race_start_info != null) return;
            var layout = new Layout().SplitColumns(new Layout("Main").SplitRows(new Layout("体力干劲条").SplitColumns(new Layout("日期").Ratio(4), new Layout("赛程倒计时").Ratio(3), new Layout("体力").Ratio(9), new Layout("干劲").Ratio(3)), new Layout("分割", new Rule()).Size(1), new Layout("训练信息").Ratio(9)).Ratio(4), new Layout("Ext").Ratio(1));
            var extInfos = new List<string>();
            var turnNum = @event.data.chara_info.turn;
            var gameYear = (turnNum - 1) / 24 + 1;
            var gameMonth = ((turnNum - 1) % 24) / 2 + 1;
            var halfMonth = (turnNum % 2 == 0) ? "后半" : "前半";
            {
                if (GameStats.currentTurn != turnNum - 1 //正常情况
                && GameStats.currentTurn != turnNum //重复显示
                && turnNum != 1 //第一个回合
                )
                {
                    GameStats.isFullGame = false;
                    AnsiConsole.MarkupLine($"[red]警告：回合数不正确，上一个回合为{GameStats.currentTurn}，当前回合为{turnNum}[/]");
                    EventLogger.Init();
                }
                else if (turnNum == 1)
                {
                    GameStats.isFullGame = true;
                    EventLogger.Init();
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
            }
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
            var supportCards = @event.data.chara_info.support_card_array.ToDictionary(x => x.position, x => x.support_card_id);
            var commandInfoArray = @event.data.home_info.command_info_array.Where(x => x.command_id > 1000);
            var trains = @event.data.sport_data_set.training_array.Chunk(5).ToArray();
            var (blue, red, yellow) = (trains[0].Sum(x => x.sport_rank) % 50, trains[1].Sum(x => x.sport_rank) % 50, trains[2].Sum(x => x.sport_rank) % 50);

            var currentVital = @event.data.chara_info.vital;
            var maxVital = @event.data.chara_info.max_vital;
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
                foreach (var item in commandInfoArray)
                    if (GameGlobal.ToTrainId.TryGetValue(item.command_id, out int value) && value == trainId)
                        foreach (var trainParam in item.params_inc_dec_info_array)
                            trainParams[trainParam.target_type] += trainParam.value;
                foreach (var item in @event.data.sport_data_set.command_info_array)
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
                trainStats[i] = stats;
            }

            var grids = new Grid();
            grids.AddColumns(5);
            var maxColor = commandInfoArray.GroupBy(x => int.Parse(x.command_id.ToString()[1].ToString())).MaxBy(x => x.Count());

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
            var commands = commandInfoArray.Select(x =>
            {
                var commandId = x.command_id.ToString();
                var command = int.Parse(commandId[3].ToString());
                var color = int.Parse(commandId[1].ToString());
                var table = new Table().AddColumn(command switch
                {
                    1 => $"速{failureRateStr[0]}",
                    2 => $"耐{failureRateStr[1]}",
                    3 => $"力{failureRateStr[2]}",
                    4 => $"根{failureRateStr[3]}",
                    5 => $"智{failureRateStr[4]}"
                });
                table.BorderColor(color switch
                {
                    1 => Color.Blue,
                    2 => Color.Red,
                    3 => Color.Yellow,
                });

                table.AddRow("当前:可获得");
                table.AddRow($"{currentFiveValueRevised[command - 1]}:{fiveValueMaxRevised[command - 1] - currentFiveValueRevised[command - 1]}");
                table.AddRow(new Rule());

                var afterVital = trainStats[command - 1].VitalGain + currentVital;
                table.AddRow(afterVital switch
                {
                    < 30 => $"体力:[red]{afterVital}[/]/{maxVital}",
                    < 50 => $"体力:[darkorange]{afterVital}[/]/{maxVital}",
                    < 70 => $"体力:[yellow]{afterVital}[/]/{maxVital}",
                    _ => $"体力:[green]{afterVital}[/]/{maxVital}"
                });
                table.AddRow($"Lv{@event.data.chara_info.training_level_info_array.First(y => y.command_id == x.command_id).level}");
                table.AddRow(new Rule());

                var stats = trainStats[command - 1];
                var score = stats.FiveValueGain.Sum();
                if (score == trainStats.Max(x => x.FiveValueGain.Sum()))
                    table.AddRow($"属:[aqua]{score}[/]|Pt:{stats.PtGain}");
                else
                    table.AddRow($"属:{score}|Pt:{stats.PtGain}");
                var gotRank = @event.data.sport_data_set.command_info_array[command - 1].gain_sport_rank_array.Sum(x => x.gain_rank);
                switch (color)
                {
                    case 1:
                        table.AddRow((blue % 50 + gotRank > 50) ? $"[blue]获得Rank:{gotRank}[/]" : $"获得Rank:{gotRank}");
                        break;
                    case 2:
                        table.AddRow((red % 50 + gotRank > 50) ? $"[red]获得Rank:{gotRank}[/]" : $"获得Rank:{gotRank}");
                        break;
                    case 3:
                        table.AddRow((yellow % 50 + gotRank > 50) ? $"[yellow]获得Rank:{gotRank}[/]" : $"获得Rank:{gotRank}");
                        break;
                }
                table.AddRow(new Rule());

                var tips = x.tips_event_partner_array.Intersect(x.training_partner_array); //红感叹号 || Hint
                var partners = x.training_partner_array
                    .Select(partner =>
                    {
                        var priority = PartnerPriority.默认;

                        // partner是当前S卡卡组的index（1~6，7是啥？我忘了）或者charaId（10xx)
                        var name = (partner >= 1 && partner <= 7 ? Database.Names.GetSupportCard(supportCards[partner]).Nickname : Database.Names.GetCharacter(partner).Nickname).EscapeMarkup();
                        var friendship = @event.data.chara_info.evaluation_info_array.First(x => x.target_id == partner).evaluation;
                        var nameColor = "[#ffffff]";
                        var nameAppend = "";
                        var shouldShining = false; // 是不是友情训练
                        if (partner >= 1 && partner <= 7)
                        {
                            priority = PartnerPriority.其他;
                            if (name.Contains("[友]")) // 友人单独标绿
                            {
                                priority = PartnerPriority.友人;
                                nameColor = $"[green]";
                            }
                            else if (friendship < 80) // 羁绊不满80，无法触发友情训练标黄
                            {
                                priority = PartnerPriority.羁绊不足;
                                nameColor = $"[yellow]";
                            }

                            //闪彩标蓝
                            {
                                //在得意位置上
                                var commandId1 = GameGlobal.ToTrainId[x.command_id];
                                shouldShining = friendship >= 80 &&
                                    name.Contains(commandId1 switch
                                    {
                                        101 => "[速]",
                                        105 => "[耐]",
                                        102 => "[力]",
                                        103 => "[根]",
                                        106 => "[智]",
                                    });

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
                        }

                        if ((partner >= 1 && partner <= 7) || (partner >= 100 && partner < 1000))//支援卡，理事长，记者，佐岳
                            if (friendship < 100) //羁绊不满100，显示羁绊
                                nameAppend += $"[red]{friendship}[/]";

                        name = $"{nameColor}{name}[/]{nameAppend}";
                        name = tips.Contains(partner) ? $"[red]![/]{name}" : name; //有Hint就加个红感叹号，和游戏内表现一样

                        return (priority, name);
                    }).OrderBy(x => x.priority);
                foreach (var (_, name) in partners)
                    table.AddRow(name);
                for (var i = 5 - partners.Count(); i > 0; i--)
                {
                    table.AddRow(string.Empty);
                }

                return new Padder(table).Padding(0, color == maxColor?.Key ? 0 : 1, 0, 0);
            });
            grids.AddRow([.. commands]);
            layout["训练信息"].Update(grids);

            if (@event.data.sport_data_set.item_id_array.Any(x => x == 6))
            {
                var groupByColorOrderByRank = @event.data.sport_data_set.command_info_array
                    .SelectMany(x => x.gain_sport_rank_array)
                    .DistinctBy(x => x.command_id)
                    .GroupBy(x => x.command_id.ToString()[1])
                    .OrderByDescending(x => x.Sum(y => y.gain_rank));
                var nonBlue = groupByColorOrderByRank.FirstOrDefault(x => x.Key != '1');
                var nonRed = groupByColorOrderByRank.FirstOrDefault(x => x.Key != '2');
                var nonYellow = groupByColorOrderByRank.FirstOrDefault(x => x.Key != '3');
                if (nonBlue != default)
                {
                    var totalRank = 0;
                    foreach (var i in nonBlue)
                    {
                        var changed = @event.data.sport_data_set.training_array.First(x => x.command_id == int.Parse($"210{i.command_id % 10}"));
                        if (changed.sport_rank + i.gain_rank > 100)
                        {
                            totalRank += 100 - changed.sport_rank;
                        }
                        else
                        {
                            totalRank += i.gain_rank;
                        }
                    }
                    if (blue + totalRank >= 50)
                        extInfos.Add($"✨{CharToColor(nonBlue.Key)}变[blue]蓝[/]可爆");
                }
                if (nonRed != default)
                {
                    var totalRank = 0;
                    foreach (var i in nonRed)
                    {
                        var changed = @event.data.sport_data_set.training_array.First(x => x.command_id == int.Parse($"220{i.command_id % 10}"));
                        if (changed.sport_rank + i.gain_rank > 100)
                        {
                            totalRank += 100 - changed.sport_rank;
                        }
                        else
                        {
                            totalRank += i.gain_rank;
                        }
                    }
                    if (red + totalRank >= 50)
                        extInfos.Add($"✨{CharToColor(nonRed.Key)}变[red]红[/]可爆");
                }
                if (nonYellow != default)
                {
                    var totalRank = 0;
                    foreach (var i in nonYellow)
                    {
                        var changed = @event.data.sport_data_set.training_array.First(x => x.command_id == int.Parse($"230{i.command_id % 10}"));
                        if (changed.sport_rank + i.gain_rank > 100)
                        {
                            totalRank += 100 - changed.sport_rank;
                        }
                        else
                        {
                            totalRank += i.gain_rank;
                        }
                    }
                    if (yellow + totalRank >= 50)
                        extInfos.Add($"✨{CharToColor(nonYellow.Key)}变[yellow]黄[/]可爆");
                }
                if (turnNum % 12 >= 9)
                    extInfos.Add("❗请及时使用相谈");
            }
            var nextRank = turnNum switch
            {
                <= 12 => 0,
                <= 24 => 10,
                <= 36 => 20,
                <= 48 => 30,
                <= 60 => 40,
                _ => 50
            };
            var lowRankSports = @event.data.sport_data_set.training_array.Where(x => x.sport_rank < nextRank);
            if (lowRankSports.Any())
            {
                foreach (var i in lowRankSports)
                {
                    var commandId = i.command_id.ToString();
                    var command = GameGlobal.TrainNames[GameGlobal.ToTrainId[i.command_id]];
                    var color = CharToColor(commandId[1]);
                    extInfos.Add($"❗{color}色的{command}等级过低");
                }
                extInfos.Add($"❗当前时期的运动等级最低为{nextRank}");
            }

            layout["日期"].Update(new Panel($"{gameYear}年 {gameMonth}月{halfMonth}").Expand());
            layout["赛程倒计时"].Update(new Panel("占位-占位").Expand());
            layout["体力"].Update(new Panel($"体力: [green]{currentVital}[/]/{maxVital}").Expand());
            layout["干劲"].Update(new Panel(@event.data.chara_info.motivation switch
            {
                5 => "[green]绝好调↑[/]",
                4 => "[yellow]好调↗[/]",
                3 => "[red]普通→[/]",
                2 => "[red]不调↘️[/]",
                1 => "[red]绝不调↓[/]"
            }).Expand());
            layout["Ext"].Update(new Panel(string.Join(Environment.NewLine, extInfos)));
            AnsiConsole.Write(layout);

            static string CharToColor(char c) =>
                c switch
                {
                    '1' => "[blue]蓝[/]",
                    '2' => "[red]红[/]",
                    '3' => "[yellow]黄[/]"
                };
        }
    }
}
