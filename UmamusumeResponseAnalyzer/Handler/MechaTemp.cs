using Gallop;
using Spectre.Console;
using System.Diagnostics.Eventing.Reader;
using System.Reflection.Metadata.Ecma335;
using System.Text;
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
            if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0) || @event.data.race_start_info != null) return;
            
            var layout = new Layout().SplitColumns(
                new Layout("Main").Size(CommandInfoLayout.Current.MainSectionWidth).SplitRows(
                    new Layout("体力干劲条").SplitColumns(
                        new Layout("日期").Ratio(4),
                        new Layout("总属性").Ratio(6),
                        new Layout("体力").Ratio(6),
                        new Layout("干劲").Ratio(3)).Size(3),
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
            var trainItems = new Dictionary<int, SingleModeCommandInfo>
            {
                { 901, @event.data.home_info.command_info_array[0] },
                { 105, @event.data.home_info.command_info_array[1] },
                { 902, @event.data.home_info.command_info_array[2] },
                { 103, @event.data.home_info.command_info_array[3] },
                { 906, @event.data.home_info.command_info_array[4] }
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
                var trainId = GameGlobal.TrainIds[i];
                var trainItem = trainItems.ElementAt(i);
                failureRate[trainId] = trainItem.Value.failure_rate;
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
                {
                    if (GameGlobal.ToTrainId.TryGetValue(item.command_id, out var value) && value == trainId)
                    {
                        foreach (var trainParam in item.params_inc_dec_info_array)
                            trainParams[trainParam.target_type] += trainParam.value;
                    }
                }

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
                // cook_data_set.command_info_array和CommandInfo，SingleCommandInfo都不一样，只能直接取
                // 目前放在1200减半之前，不知道对不对
                var gainUpper = dataset.command_info_array.FirstOrDefault(x => x.command_id == trainId || x.command_id == GameGlobal.XiahesuIds[trainId])?.params_inc_dec_info_array;
                if (gainUpper != null)
                {
                    foreach (var item in gainUpper)
                        if (item.target_type == 30)
                            stats.PtGain += item.value;
                        else if (item.target_type <= 5)
                            stats.FiveValueGain[item.target_type - 1] += item.value;
                        else
                            AnsiConsole.MarkupLine("[red]here[/]");
                }

                for (var j = 0; j < 5; j++)
                    stats.FiveValueGain[j] = ScoreUtils.ReviseOver1200(turn.Stats[j] + stats.FiveValueGain[j]) - ScoreUtils.ReviseOver1200(turn.Stats[j]);

                trainStats[i] = stats;
            }

            var grids = new Grid();
            grids.AddColumns(5);

            var failureRateStr = new string[5];
            //失败率>=40%标红、>=20%(有可能大失败)标DarkOrange、>0%标黄
            for (var i = 0; i < 5; i++)
            {
                var thisFailureRate = failureRate[GameGlobal.TrainIds[i]];
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

                table.AddRow($"Lv{command.TrainLevel} | ");
                table.AddRow(new Rule());

                var stats = trainStats[command.TrainIndex - 1];
                var score = stats.FiveValueGain.Sum();
                if (score == trainStats.Max(x => x.FiveValueGain.Sum()))
                    table.AddRow($"{I18N_StatSimple}:[aqua]{score}[/]|Pt:{stats.PtGain}");
                else
                    table.AddRow($"{I18N_StatSimple}:{score}|Pt:{stats.PtGain}");

                foreach (var trainingPartner in command.TrainingPartners)
                {
                    table.AddRow(trainingPartner.Name);
                    if (trainingPartner.Shining)
                        table.BorderColor(Color.LightGreen);
                }
                for (var i = 5 - command.TrainingPartners.Count(); i > 0; i--)
                {
                    table.AddRow(string.Empty);
                }
                table.AddRow(new Rule());

                return new Padder(table).Padding(0, 0, 0, 0);
            }); // foreach command
            grids.AddRow([.. commands]);
            layout["训练信息"].Update(grids);

            // 额外信息
            var exTable = new Table().AddColumn("Extras");
            exTable.HideHeaders();

            layout["日期"].Update(new Panel($"{turn.Year}{I18N_Year} {turn.Month}{I18N_Month}{turn.HalfMonth}").Expand());
            layout["总属性"].Update(new Panel($"[cyan]总属性: {totalValue}[/]").Expand());
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
            layout["重要信息"].Update(new Panel(string.Join(Environment.NewLine, critInfos)).Expand());

            layout["Ext"].Update(exTable);
            AnsiConsole.Write(layout);
            // 光标倒转一点
            AnsiConsole.Cursor.SetPosition(0, 31);
        }
    }
}
