using Gallop;
using Newtonsoft.Json;
using Spectre.Console;
using UmamusumeResponseAnalyzer.AI;
using UmamusumeResponseAnalyzer.Communications.Subscriptions;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using UmamusumeResponseAnalyzer.LocalizedLayout.Handlers;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static UmamusumeResponseAnalyzer.Localization.CommandInfo.Cook;
using static UmamusumeResponseAnalyzer.Localization.CommandInfo.UAF;
using static UmamusumeResponseAnalyzer.Localization.Game;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseCookCommandInfo(Gallop.SingleModeCheckEventResponse @event)
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
            var critInfos = new List<string>();
            var turn = new TurnInfoCook(@event.data);
            var eventCookDataset = @event.data.cook_data_set;

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
                { 101, @event.data.home_info.command_info_array[0] },
                { 105, @event.data.home_info.command_info_array[1] },
                { 102, @event.data.home_info.command_info_array[2] },
                { 103, @event.data.home_info.command_info_array[3] },
                { 106, @event.data.home_info.command_info_array[4] }
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
            var currentFiveValueRevised = currentFiveValue.Select(x => ScoreUtils.ReviseOver1200(x)).ToArray();
            var totalValue = currentFiveValueRevised.Sum();
            var totalValueWithPt = totalValue + @event.data.chara_info.skill_point;

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
                {
                    if (GameGlobal.ToTrainId.TryGetValue(item.command_id, out var value) && value == trainId)
                    {
                        foreach (var trainParam in item.params_inc_dec_info_array)
                            trainParams[trainParam.target_type] += trainParam.value;
                    }
                }

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
                stats.PtGain = trainParams[30];

                // 取上半数值
                // cook_data_set.command_info_array和CommandInfo，SingleCommandInfo都不一样，只能直接取
                // 目前放在1200减半之前，不知道对不对
                var cookValueGainUpper = eventCookDataset.command_info_array.FirstOrDefault(x => x.command_id == trainId || x.command_id == GameGlobal.XiahesuIds[trainId])?.params_inc_dec_info_array;
                if (cookValueGainUpper != null)
                {
                    foreach (var item in cookValueGainUpper)
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

                var commandMaterial = turn.CommandMaterials.FirstOrDefault(x => x.command_id == command.CommandId);
                var totalMaterialsBeforeClick = turn.Harvests.Sum(x => x.harvest_num);
                var totalMaterials = commandMaterial?.material_harvest_info_array.Sum(x => x.harvest_num) - totalMaterialsBeforeClick ?? 0;

                table.AddRow($"Lv{command.TrainLevel} | 材+{totalMaterials}");
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
                var matText = GameGlobal.CookMaterialName[command.TrainIndex-1];
                var matCurrent = turn.Materials[command.TrainIndex - 1].num;
                var matMax = turn.Facilities[command.TrainIndex - 1].facility_level * 200;
                var matHarvestBeforeClick = turn.Harvests[command.TrainIndex - 1].harvest_num;
                var matHarvest = commandMaterial?.material_harvest_info_array[command.TrainIndex - 1].harvest_num ?? matHarvestBeforeClick;
                Style matColor;
                if (matCurrent + matHarvest >= matMax)
                    matColor = new Style(Color.Red);
                else if (matCurrent + matHarvest > matMax * 0.85)
                    matColor = new Style(Color.Yellow);
                else
                    matColor = new Style();

                table.AddRow(new Markup($"{matText}: {matCurrent}/{matMax}", matColor));
                table.AddRow(new Markup($"{I18N_Harvest}: +{matHarvestBeforeClick}/+{matHarvest - matHarvestBeforeClick}", matColor));

                return new Padder(table).Padding(0, 0, 0, 0);
            }); // foreach command
            grids.AddRow([.. commands]);
            layout["训练信息"].Update(grids);

            // 额外信息
            var exTable = new Table().AddColumn("Extras");
            exTable.HideHeaders();

            // 田地相关
            if (turn.Facilities.Any(x => x.facility_id != 200 && GameGlobal.CookGardenLevelUpCost[x.facility_level] <= eventCookDataset.cook_info.care_point))
            {
                critInfos.Add("[yellow]** 田地可升级 **[/]");
            }
            if (@event.data.chara_info.chara_effect_id_array.Any(x => x == 32))
            {
                critInfos.Add("[lightgreen]【休息的心得】生效中[/]");
            }
            exTable.AddRow($"农田Pt: {eventCookDataset.cook_info.care_point} (+{eventCookDataset.care_point_gain_num})");
            exTable.AddRow($"料理Pt: {eventCookDataset.cook_info.cooking_friends_power}");
            exTable.AddRow("");
            foreach (var item in turn.CommandMaterials)
            {
                // 考虑放进资源文件
                var actionName = item.command_type switch
                {
                    3 => "出行",
                    4 => "比赛",
                    7 => "休息",
                    8 => "治疗",
                    _ => null
                };
                if (actionName == null) continue;

                var sty = ((item.boost_type == 2 || item.boost_type == 4) ? new Style(Color.Green) : new Style());
                var matHarvest = item.material_harvest_info_array[item.material_id / 100 - 1].harvest_num;
                var matHarvestBeforeClick = turn.Harvests[item.material_id / 100 - 1].harvest_num;
                var matText = GameGlobal.CookMaterialName[item.material_id / 100 - 1];
                exTable.AddRow(new Markup($"{actionName}: {matText} +{matHarvest - matHarvestBeforeClick}", sty));
            }
            exTable.AddRow(new Rule());
            if (eventCookDataset.dish_info == null)
            {
                // 没吃饭时显示
                if (eventCookDataset.cook_info.cooking_success_point >= eventCookDataset.cook_info.cooking_success_base_point ||
                eventCookDataset.cooking_success_rate == 100)
                {
                    exTable.AddRow("[yellow]必定大成功[/]");
                }
                else
                {
                    exTable.AddRow($"料理成功率: {eventCookDataset.cooking_success_rate}%");
                    exTable.AddRow($"进度 {eventCookDataset.cook_info.cooking_success_point}/{eventCookDataset.cook_info.cooking_success_base_point}");
                }
            }
            else
            {
                // 吃饭时显示
                var dish = eventCookDataset.dish_info;
                var sty = new Style();
                var dishName = GameGlobal.CookDishName[dish.dish_id];
                if (dish.cooking_result_state == 2)
                {
                    sty = new Style(Color.Yellow);
                    dishName += " HQ";
                }

                exTable.AddRow(new Markup($"当前料理: {dishName}", sty));
                exTable.AddRow("效果:");
                // 基础效果
                foreach (var item in dish.dish_effect_info_array)
                {
                    exTable.AddRow($"{GameGlobal.CookEffectName[item.effect_type]}+{item.effect_value_1}");
                }
                // 基础料理补一个词条
                if (dish.dish_id <= 2)
                    exTable.AddRow("羁绊+2");

                // 额外词条。不包含农田效果，和点哪个训练相关不计算了
                if (eventCookDataset.success_effect_id_array != null)
                {
                    foreach (var x in eventCookDataset.success_effect_id_array)
                    {
                        if (x <= 15)  // 普通额外词条
                            exTable.AddRow($"{GameGlobal.CookSuccessEffect[(x - 1) % 5]}");
                        else
                        {
                            // G1Plate 固定额外词条
                            exTable.AddRow("心情+1, 分身+2");
                            break;
                        }
                    }
                }
            }
            // 计算G1Plate余量
            if (turn.Turn >= 69)
            {
                exTable.AddRow(new Rule());
                
                var isCooked = (eventCookDataset.dish_info != null); // 本回合吃了吗
                var turnRemainUra = Math.Min(6, 78 - turn.Turn);  // 还剩几个有菜的URA回合
                var g1PlateNeeded = Math.Min(6, turnRemainUra + (isCooked ? 0 : 1));  // 还需要做几个G1Plate
                var turnRemainNormal = 72 - turn.Turn + 1;                
                // 排除72回合行动后的情况
                if (turn.Turn == 72 && eventCookDataset.care_history_info_array.Length == 0)
                    --turnRemainNormal;
                
                exTable.AddRow($"剩 [lightgreen]{g1PlateNeeded}[/] 个GI料理");
                exTable.AddRow("食材溢出量：");

                var matRemain = new int[5];    // 最终溢出菜量
                var normalRate = (turnRemainNormal > 0 ? 160.0 / eventCookDataset.care_point_gain_num: 0.0); 
                for (var i = 0; i < 5; ++i)
                {
                    var baseHarvest = GameGlobal.CookGardenBaseHarvest[turn.Facilities[i].facility_level-1];  // 基础收获量
                    var normalHarvest = (int)Math.Floor(turn.Harvests[i].harvest_num * normalRate);  // 连续绿圈，72回合预计能收多少，>72时为0
                    var uraBaseHarvest = (int)Math.Floor(baseHarvest * 0.75);  // URA绿圈，以当前设施等级，菜的基础增加量
                    matRemain[i] = turn.Materials[i].num + normalHarvest + uraBaseHarvest * turnRemainUra - g1PlateNeeded * 80;

                    var sty = new Style(Color.Green);
                    if (matRemain[i] < -80)
                        sty = new Style(Color.Red);
                    else if (matRemain[i] < 0)
                        sty = new Style(Color.Yellow);
                    exTable.AddRow(new Markup($"{GameGlobal.CookMaterialName[i]}: {matRemain[i].ToString("+#;-#;0")}", sty));
                }
            }
            // 计算连续事件表现
            var eventPerf = EventLogger.PrintCardEventPerf(@event.data.chara_info.scenario_id);
            if (eventPerf.Count > 0)
            {
                exTable.AddRow(new Rule());
                foreach (var row in eventPerf)
                    exTable.AddRow(new Markup(row));
            }

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

            var availableTrainingCount = @event.data.home_info.command_info_array.Count(x => x.is_enable == 1);
            //AnsiConsole.MarkupLine($"[aqua]{availableTrainingCount}[/]");
            if (availableTrainingCount <= 1)
            {
                critInfos.Add("[aqua]非训练回合[/]");
            }
            layout["重要信息"].Update(new Panel(string.Join(Environment.NewLine, critInfos)).Expand());
           
            layout["Ext"].Update(exTable);
            AnsiConsole.Write(layout);
            // 光标倒转一点
            AnsiConsole.Cursor.SetPosition(0, 31);

            if (@event.IsScenario(ScenarioType.Cook))
            {
                var gameStatusToSend = new GameStatusSend_Cook(@event);
                if (gameStatusToSend.islegal == false)
                {
                    return;
                }

                //Console.Write(gameStatusToSend);
                var wsCount = SubscribeAiInfo.Signal(gameStatusToSend);
                if (wsCount > 0 && !critInfos.Contains(I18N_RepeatTurn))    // hack判断一下是否重复显示，是否已经连接AI
                    AnsiConsole.MarkupLine("\n[aqua]AI计算中...[/]");
                //if (Config.Get(Localization.Config.I18N_WriteAIInfo))
                if(true)
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
            } // if
        }
    }
}
