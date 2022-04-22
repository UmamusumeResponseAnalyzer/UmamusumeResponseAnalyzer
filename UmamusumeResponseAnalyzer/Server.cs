using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Net;
using System.Runtime.InteropServices;
using UmamusumeResponseAnalyzer.Localization;

namespace UmamusumeResponseAnalyzer
{
    internal static class Server
    {
        static readonly HttpListener httpListener = new();
        static readonly object _lock = new();
        public static void Start()
        {
            httpListener.Prefixes.Add("http://127.0.0.1:4693/");
            httpListener.Start();
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                httpListener.Close();
            };
            Task.Run(async () =>
            {
                while (httpListener.IsListening)
                {
                    var ctx = await httpListener.GetContextAsync();

                    var ms = new MemoryStream();
                    ctx.Request.InputStream.CopyTo(ms);
                    var buffer = ms.ToArray();

                    if (ctx.Request.RawUrl == "/notify/response")
                    {
#if DEBUG
                        Directory.CreateDirectory("packets");
                        File.WriteAllBytes($@"./packets/{DateTime.Now:yy-MM-dd HH-mm-ss}R.bin", buffer);
                        File.WriteAllText($@"./packets/{DateTime.Now:yy-MM-dd HH-mm-ss}R.json", JObject.Parse(MessagePackSerializer.ConvertToJson(buffer)).ToString());
#endif
                        _ = Task.Run(() => ParseResponse(buffer));
                    }
                    else if (ctx.Request.RawUrl == "/notify/request")
                    {
#if DEBUG
                        Directory.CreateDirectory("packets");
                        File.WriteAllText($@"./packets/{DateTime.Now:yy-MM-dd HH-mm-ss}Q.json", JObject.Parse(MessagePackSerializer.ConvertToJson(buffer.AsMemory()[170..])).ToString());
#endif
                        _ = Task.Run(() => ParseRequest(buffer[170..]));
                    }

                    await ctx.Response.OutputStream.WriteAsync(Array.Empty<byte>());
                    ctx.Response.Close();
                }
            });
        }
        static void ParseRequest(byte[] buffer)
        {
            try
            {
                lock (_lock)
                {
                    var str = MessagePackSerializer.ConvertToJson(buffer);
                    switch (str)
                    {
                        default:
                            return;
                    }
                }
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
            }
        }
        static void ParseResponse(byte[] buffer)
        {
            try
            {
                lock (_lock)
                {
                    var str = MessagePackSerializer.ConvertToJson(buffer);
                    switch (str)
                    {
                        case var CheckEvent when (str.Contains("chara_info") && str.Contains("race_condition_array")) || str.Contains("unchecked_event_array"):
                            if (Config.Get(Resource.ConfigSet_ParseSingleModeCheckEventResponse))
                                ParseSingleModeCheckEventResponse(buffer);
                            if (Config.Get(Resource.ConfigSet_MaximiumGradeSkillRecommendation) && str.Contains("skill_tips_array"))
                                ParseSkillTipsResponse(buffer);
                            if (Config.Get(Resource.ConfigSet_ShowCommandInfo))
                                ParseCommandInfo(buffer);
                            break;
                        case var TrainedCharaLoad when str.Contains("trained_chara_array") && str.Contains("trained_chara_favorite_array") && str.Contains("room_match_entry_chara_id_array"):
                            if (Config.Get(Resource.ConfigSet_ParseTrainedCharaLoadResponse))
                                ParseTrainedCharaLoadResponse(buffer);
                            break;
                        case var FriendSearch when str.Contains("friend_info") && str.Contains("user_info_summary") && str.Contains("practice_partner_info") && str.Contains("directory_card_array") && str.Contains("support_card_data") && str.Contains("release_num_info") && str.Contains("trophy_num_info") && str.Contains("team_stadium_user") && str.Contains("follower_num") && str.Contains("own_follow_num") && str.Contains("enable_circle_scout"):
                            if (Config.Get(Resource.ConfigSet_ParseFriendSearchResponse))
                                ParseFriendSearchResponse(buffer);
                            break;
                        case var TeamStadiumOpponentList when str.Contains("opponent_info_array"):
                            if (Config.Get(Resource.ConfigSet_ParseTeamStadiumOpponentListResponse))
                                ParseTeamStadiumOpponentListResponse(buffer.Replace(new byte[] { 0x88, 0xC0, 0x01 }, new byte[] { 0x87 }));
                            break;
                        case var PracticeRaceRaceStartResponse when str.Contains("trained_chara_array") && str.Contains("race_result_info") && str.Contains("entry_info_array") && str.Contains("practice_race_id") && str.Contains("state") && str.Contains("practice_partner_owner_info_array"):
                            if (Config.Get(Resource.ConfigSet_ParsePracticeRaceRaceStartResponse))
                                ParsePracticeRaceRaceStartResponse(buffer);
                            break;
                        case var RoomMatchRaceStartResponse when str.Contains("race_scenario") && str.Contains("random_seed") && str.Contains("race_horse_data_array") && str.Contains("trained_chara_array") && str.Contains("season") && str.Contains("weather") && str.Contains("ground_condition"):
                            if (Config.Get(Resource.ConfigSet_ParseRoomMatchRaceStartResponse))
                                ParseRoomMatchRaceStartResponse(buffer);
                            break;
                        default:
                            return;
                    }
                }
            }
            catch (Exception)
            {
            }
        }
        static void ParseCommandInfo(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.SingleModeCheckEventResponse>(buffer);
            if (@event != default && @event.data.home_info != null && @event.data.home_info.command_info_array != null && @event.data.home_info.command_info_array.Length != 0)
            {
                if (@event.data.unchecked_event_array == null || @event.data.unchecked_event_array.Length > 0 | @event.data.race_start_info != null) return;
                var failureRate = new Dictionary<int, int>
                {
                    {101,@event.data.home_info.command_info_array.Any(x => x.command_id == 601) ? @event.data.home_info.command_info_array.First(x => x.command_id == 601).failure_rate : @event.data.home_info.command_info_array.First(x => x.command_id == 101).failure_rate },
                    {105,@event.data.home_info.command_info_array.Any(x => x.command_id == 602) ? @event.data.home_info.command_info_array.First(x => x.command_id == 602).failure_rate : @event.data.home_info.command_info_array.First(x => x.command_id == 105).failure_rate },
                    {102,@event.data.home_info.command_info_array.Any(x => x.command_id == 603) ? @event.data.home_info.command_info_array.First(x => x.command_id == 603).failure_rate : @event.data.home_info.command_info_array.First(x => x.command_id == 102).failure_rate },
                    {103,@event.data.home_info.command_info_array.Any(x => x.command_id == 604) ? @event.data.home_info.command_info_array.First(x => x.command_id == 604).failure_rate : @event.data.home_info.command_info_array.First(x => x.command_id == 103).failure_rate },
                    {106,@event.data.home_info.command_info_array.Any(x => x.command_id == 605) ? @event.data.home_info.command_info_array.First(x => x.command_id == 605).failure_rate : @event.data.home_info.command_info_array.First(x => x.command_id == 106).failure_rate }
                };
                var table = new Table();
                table.AddColumns(
                    $"速({(failureRate[101] > 16 ? $"[red]{failureRate[101]}[/]" : failureRate[101])}%)"
                    , $"耐({(failureRate[105] > 16 ? $"[red]{failureRate[105]}[/]" : failureRate[105])}%)"
                    , $"力({(failureRate[102] > 16 ? $"[red]{failureRate[102]}[/]" : failureRate[102])}%)"
                    , $"根({(failureRate[103] > 16 ? $"[red]{failureRate[103]}[/]" : failureRate[103])}%)"
                    , $"智({(failureRate[106] > 16 ? $"[red]{failureRate[106]}[/]" : failureRate[106])}%)");
                var supportCards = @event.data.chara_info.support_card_array.ToDictionary(x => x.position, x => x.support_card_id);
                var commandInfo = new Dictionary<int, string[]>();
                foreach (var command in @event.data.home_info.command_info_array)
                {
                    if (command.command_id != 101 && command.command_id != 105 && command.command_id != 102 && command.command_id != 103 && command.command_id != 106 &&
                        command.command_id != 601 && command.command_id != 602 && command.command_id != 603 && command.command_id != 604 && command.command_id != 605) continue;
                    var tips = command.tips_event_partner_array.Intersect(command.training_partner_array);
                    commandInfo.Add(command.command_id, command.training_partner_array
                        .Select(partner =>
                        {
                            var name = Database.SupportIdToShortName[(partner >= 1 && partner <= 7) ? supportCards[partner] : partner].EscapeMarkup();
                            if (partner >= 1 && partner <= 7)
                            {
                                if (@event.data.chara_info.evaluation_info_array.First(x => x.target_id == partner).evaluation < 80)
                                    name = $"[yellow]{name}[/]";
                            }
                            if (name.Contains("[友]"))
                                name = $"[green]{name}[/]";
                            return tips.Contains(partner) ? $"[red]![/]{name}" : name;
                        }).ToArray());
                }
                if (!commandInfo.SelectMany(x => x.Value).Any()) return;
                for (var i = 0; i < 5; ++i)
                {
                    var rows = new List<string>();
                    foreach (var j in commandInfo)
                        if (j.Value.Length > i)
                        {

                            rows.Add(IsShining(j.Key, j.Value[i]));
                        }
                        else
                        {
                            rows.Add(string.Empty);
                        }
                    table.AddRow(rows.ToArray());
                }
                AnsiConsole.Write(table);

                static string IsShining(int commandId, string card)
                {
                    return card.Contains(commandId switch
                    {
                        101 => "[速]",
                        105 => "[耐]",
                        102 => "[力]",
                        103 => "[根]",
                        106 => "[智]",
                        601 => "[速]",
                        602 => "[耐]",
                        603 => "[力]",
                        604 => "[根]",
                        605 => "[智]",
                    }) ? $"[aqua]{card}[/]" : card;
                }
            }
        }
        static void ParseSingleModeCheckEventResponse(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.SingleModeCheckEventResponse>(buffer);
            if (@event != default)
            {
                if (@event.data.unchecked_event_array?.Length > 0)
                {
                    foreach (var i in @event.data.unchecked_event_array)
                    {
                        //if (i.event_contents_info?.choice_array.Length == 0) continue;
                        if (Database.Events.ContainsKey(i.story_id))
                        {
                            var mainTree = new Tree(Database.Events[i.story_id].TriggerName.EscapeMarkup());
                            var eventTree = new Tree(Database.Events[i.story_id].Name.EscapeMarkup());
                            for (var j = 0; j < i?.event_contents_info?.choice_array.Length; ++j)
                            {
                                var tree = new Tree($"{(string.IsNullOrEmpty(Database.Events[i.story_id].Choices[j].Option) ? Resource.SingleModeCheckEvent_Event_NoOption : Database.Events[i.story_id].Choices[j].Option)} @ {i.event_contents_info.choice_array[j].select_index}".EscapeMarkup());
                                if (Database.SuccessEvent.TryGetValue(Database.Events[i.story_id].Name, out var successEvent))
                                    AddSuccessEvent(successEvent.Choices.Where(x => (x.ChoiceIndex == j + 1)));
                                else
                                    AddNormalEvent();
                                eventTree.AddNode(tree);

                                void AddSuccessEvent(IEnumerable<SuccessChoice> successChoices)
                                {
                                    if (!successChoices.Any())
                                    {
                                        AddNormalEvent();
                                        return;
                                    }
                                    var successChoice = successChoices.FirstOrDefault(x => x.SelectIndex == i.event_contents_info.choice_array[j].select_index);
                                    if (successChoice != default && successChoice.Effects.ContainsKey(@event.data.chara_info.scenario_id))
                                        tree.AddNode($"[mediumspringgreen on #081129]{(string.IsNullOrEmpty(successChoice.Effects[@event.data.chara_info.scenario_id]) ? Database.Events[i.story_id].Choices[j].SuccessEffect : successChoice.Effects[@event.data.chara_info.scenario_id]).EscapeMarkup()}[/]");
                                    else
                                        tree.AddNode($"[#FF0050 on #081129]{Database.Events[i.story_id].Choices[j].FailedEffect.EscapeMarkup()}[/]");
                                }
                                void AddNormalEvent()
                                {
                                    if (string.IsNullOrEmpty(Database.Events[i.story_id].Choices[j].FailedEffect) || Database.Events[i.story_id].Choices[j].FailedEffect == "-")
                                        tree.AddNode($"{Database.Events[i.story_id].Choices[j].SuccessEffect}".EscapeMarkup());
                                    else
                                        tree.AddNode($"{Database.Events[i.story_id].Choices[j].FailedEffect}".EscapeMarkup());
                                }
                            }
                            mainTree.AddNode(eventTree);
                            AnsiConsole.Write(mainTree);
                        }
                        else
                        {
                            File.AppendAllLines(Database.UNKNOWN_EVENT_FILEPATH, new string[] { i.story_id.ToString() });
                            var mainTree = new Tree(Resource.SingleModeCheckEvent_Event_UnknownSource);
                            var eventTree = new Tree(Resource.SingleModeCheckEvent_Event_UnknownEvent);
                            for (var j = 0; j < i?.event_contents_info?.choice_array.Length; ++j)
                            {
                                var tree = new Tree(string.Format(Resource.SingleModeCheckEvent_Event_UnknownOption, i.event_contents_info.choice_array[j].select_index));
                                tree.AddNode(Resource.SingleModeCheckEvent_Event_UnknownEffect);
                                eventTree.AddNode(tree);
                            }
                            mainTree.AddNode(eventTree);
                            AnsiConsole.Write(mainTree);
                        }
                    }
                }
            }
        }
        static void ParseSkillTipsResponse(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.SingleModeCheckEventResponse>(buffer);
            if (@event != default && (@event.data.chara_info.state == 2 || @event.data.chara_info.state == 3) && @event.data.unchecked_event_array?.Length == 0)
            {
                var totalSP = @event.data.chara_info.skill_point;
                var tips = @event.data.chara_info.skill_tips_array
                    .Select(x => Database.Skills[(x.group_id, x.rarity)].Select(y => y.Apply(@event.data.chara_info, x.level)))
                    .SelectMany(x => x)
                    .Where(x => x.Rate > 0)
                    .OrderByDescending(x => x.Grade / (double)x.TotalCost)
                    .ToList();
                var learn = new List<SkillManager.SkillData>();
                do
                {
                    if (learn.Any()) { learn.Clear(); totalSP = @event.data.chara_info.skill_point; }
                    int[][] Matrix = new int[tips.Count][];
                    int[][] Picks = new int[tips.Count][];
                    for (var i = 0; i < Matrix.Length; i++) { Matrix[i] = new int[totalSP + 1]; }
                    for (var i = 0; i < Picks.Length; i++) { Picks[i] = new int[totalSP + 1]; }
                    Recursive(tips.Count - 1, totalSP, 1);
                    for (var i = tips.Count - 1; i >= 0 && totalSP >= 0; --i)
                    {
                        if (Picks[i][totalSP] == 1)
                        {
                            totalSP -= tips[i].TotalCost;
                            learn.Add(tips[i]);
                        }
                    }
                    foreach (var i in learn.GroupBy(x => x.GroupId).Where(x => x.Count() > 1))
                    {
                        var duplicated = learn.Where(x => x.GroupId == i.Key);
                        foreach (var j in duplicated)
                        {
                            if (j.Superior != null)
                            {
                                tips.Remove(j);
                            }
                        }
                    }

                    // 0/1 knapsack problem
                    int Recursive(int i, int w, int depth)
                    {
                        var take = 0;
                        if (Matrix[i][w] != 0) { return Matrix[i][w]; }

                        if (i == 0)
                        {
                            if (tips[i].TotalCost <= w)
                            {
                                Picks[i][w] = 1;
                                Matrix[i][w] = tips[0].Grade;
                                return tips[i].Grade;
                            }

                            Picks[i][w] = -1;
                            Matrix[i][w] = 0;
                            return 0;
                        }

                        if (tips[i].TotalCost <= w)
                        {
                            take = tips[i].Grade + Recursive(i - 1, w - tips[i].TotalCost, depth + 1);
                        }

                        var dontTake = Recursive(i - 1, w, depth + 1);

                        Matrix[i][w] = Math.Max(take, dontTake);
                        if (take > dontTake)
                        {
                            Picks[i][w] = 1;
                        }
                        else
                        {
                            Picks[i][w] = -1;
                        }

                        return Matrix[i][w];
                    }
                } while (learn.GroupBy(x => x.GroupId).Any(x => x.Count() > 1)); //懒
                learn = learn.OrderBy(x => x.DisplayOrder).ToList();

                var table = new Table();
                table.Title(string.Format(Resource.MaximiumGradeSkillRecommendation_Title, @event.data.chara_info.skill_point, @event.data.chara_info.skill_point - totalSP, totalSP));
                table.AddColumns(Resource.MaximiumGradeSkillRecommendation_Columns_SkillName, Resource.MaximiumGradeSkillRecommendation_Columns_RequireSP, Resource.MaximiumGradeSkillRecommendation_Columns_Grade);
                table.Columns[0].Centered();
                foreach (var i in learn)
                {
                    table.AddRow($"{i.Name}", $"{i.TotalCost}", $"{i.Grade}");
                }
                var statusPoint = Database.StatusToPoint[@event.data.chara_info.speed]
                                + Database.StatusToPoint[@event.data.chara_info.stamina]
                                + Database.StatusToPoint[@event.data.chara_info.power]
                                + Database.StatusToPoint[@event.data.chara_info.guts]
                                + Database.StatusToPoint[@event.data.chara_info.wiz];
                var previousLearnPoint = 0;
                foreach (var i in @event.data.chara_info.skill_array)
                {
                    if (i.skill_id >= 1000000) continue; // Carnival bonus
                    if (i.skill_id.ToString()[..1] == "1" && i.skill_id > 100000) //3*固有
                    {
                        previousLearnPoint += 170 * i.level;
                    }
                    else if (i.skill_id.ToString().Length == 5) //2*固有
                    {
                        previousLearnPoint += 120 * i.level;
                    }
                    else
                    {
                        previousLearnPoint += Database.Skills[i.skill_id] == null ? Database.Skills[i.skill_id].Grade : 0;
                    }
                }
                var totalPoint = learn.Sum(x => x.Grade) + previousLearnPoint + statusPoint;
                table.Caption(string.Format(Resource.MaximiumGradeSkillRecommendation_Caption, previousLearnPoint, learn.Sum(x => x.Grade), statusPoint, totalPoint, Database.GradeToRank.First(x => x.Min < totalPoint && totalPoint < x.Max).Rank));
                AnsiConsole.Write(table);
            }
        }
        static void ParseTrainedCharaLoadResponse(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.TrainedCharaLoadResponse>(buffer);
            var data = @event?.data;
            if (@event != default && data?.trained_chara_array.Length > 0 && data.trained_chara_favorite_array.Length > 0)
            {
                var fav_ids = data.trained_chara_favorite_array.Select(x => x.trained_chara_id).ToList();
                var chara = data.trained_chara_array.Where(x => fav_ids.Contains(x.trained_chara_id));
                var win_saddle_result = new List<(string Name, int WinSaddle, int Score)>();
                foreach (var i in chara)
                    win_saddle_result.Add((Database.IdToName[i.card_id], i.win_saddle_id_array.Intersect(i.succession_chara_array[0].win_saddle_id_array).Count()
                    + i.win_saddle_id_array.Intersect(i.succession_chara_array[1].win_saddle_id_array).Count(), i.rank_score));
                win_saddle_result.Sort((a, b) => b.WinSaddle.CompareTo(a.WinSaddle));
                var table = new Table
                {
                    Border = TableBorder.Ascii
                };
                table.AddColumns("种马名", "胜鞍加成", "分数");
                foreach (var (Name, WinSaddle, Score) in win_saddle_result)
                    table.AddRow(Name.EscapeMarkup(), WinSaddle.ToString(), Score.ToString());
                AnsiConsole.Write(table);
            }
        }
        static void ParseFriendSearchResponse(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.FriendSearchResponse>(buffer);
            var data = @event?.data;
            if (@event != default && data != default)
            {
                var i = data.practice_partner_info;
                var (Name, WinSaddle, Score) = (Database.IdToName?[i.card_id], i.win_saddle_id_array.Intersect(i.succession_chara_array[0].win_saddle_id_array).Count()
                        + i.win_saddle_id_array.Intersect(i.succession_chara_array[1].win_saddle_id_array).Count(), i.rank_score);
                AnsiConsole.Write(new Rule());
                AnsiConsole.WriteLine($"好友：{data.user_info_summary.name}\t\tID：{data.user_info_summary.viewer_id}\t\tFollower数：{data.follower_num}");
                AnsiConsole.WriteLine($"种马：{Name}\t\t{WinSaddle}\t\t{Score}");
                AnsiConsole.Write(new Rule());
            }
        }
        static void ParseTeamStadiumOpponentListResponse(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.TeamStadiumOpponentListResponse>(buffer);
            var data = @event?.data;
            if (data == default) return;
            var container = new Table
            {
                Border = TableBorder.Double
            };
            container.AddColumn(new TableColumn(string.Empty).NoWrap());
            container.HideHeaders();
            foreach (var i in data.opponent_info_array.OrderByDescending(x => x.strength))
            {
                var Type = i.strength switch
                {
                    1 => "上",
                    2 => "中",
                    3 => "下"
                };
                var table = new Table();
                table.Title(Type);
                table.AddColumns(Enumerable.Repeat(new TableColumn("　　　").NoWrap(), 17).ToArray());
                table.HideHeaders();
                var teamData = i.team_data_array.GroupBy(x => x.distance_type).ToDictionary(x => x.Key, x => x.ToList());
                var properTypeLine = new List<string> { string.Empty };
                var properValueLine = new List<string> { "适性" };
                var speedLine = new List<string> { "速度" };
                var staminaLine = new List<string> { "耐力" };
                var powerLine = new List<string> { "力量" };
                var gutsLine = new List<string> { "根性" };
                var wizLine = new List<string> { "智力" };
                foreach (var j in teamData)
                {
                    foreach (var k in j.Value)
                    {
                        var trainedChara = i.trained_chara_array.First(x => x.trained_chara_id == k.trained_chara_id);
                        var properType = string.Empty;
                        var properValue = string.Empty;
                        properType += (k.distance_type switch
                        {
                            5 => "泥",
                            _ => "芝"
                        });
                        properValue += (k.distance_type switch
                        {
                            5 => GetProper(trainedChara.proper_ground_dirt),
                            _ => GetProper(trainedChara.proper_ground_turf)
                        });
                        properValue += ' ';
                        properType += (k.distance_type switch
                        {
                            1 => "短",
                            2 => "英",
                            3 => "中",
                            4 => "长",
                            5 => "英"
                        });
                        properValue += (k.distance_type switch
                        {
                            1 => GetProper(trainedChara.proper_distance_short),
                            2 => GetProper(trainedChara.proper_distance_mile),
                            3 => GetProper(trainedChara.proper_distance_middle),
                            4 => GetProper(trainedChara.proper_distance_long),
                            5 => GetProper(trainedChara.proper_distance_mile)
                        });
                        properValue += ' ';
                        properType += (k.running_style switch
                        {
                            1 => "逃",
                            2 => "先",
                            3 => "差",
                            4 => "追"
                        });
                        properValue += (k.running_style switch
                        {
                            1 => GetProper(trainedChara.proper_running_style_nige),
                            2 => GetProper(trainedChara.proper_running_style_senko),
                            3 => GetProper(trainedChara.proper_running_style_sashi),
                            4 => GetProper(trainedChara.proper_running_style_oikomi)
                        });
                        properTypeLine.Add(properType);
                        properValueLine.Add(properValue);
                        speedLine.Add(trainedChara.speed.ToString());
                        staminaLine.Add(trainedChara.stamina.ToString());
                        powerLine.Add(trainedChara.power.ToString());
                        gutsLine.Add(trainedChara.guts.ToString());
                        wizLine.Add(trainedChara.wiz.ToString());
                    }
                }
                table.AddRow(properTypeLine.Append("平 均").ToArray());
                table.AddRow(properValueLine.Append("/ / /").ToArray());
                table.AddRow(speedLine.Append(speedLine.Skip(1).Average(x => int.Parse(x)).ToString("F0")).ToArray());
                table.AddRow(staminaLine.Append(staminaLine.Skip(1).Average(x => int.Parse(x)).ToString("F0")).ToArray());
                table.AddRow(powerLine.Append(powerLine.Skip(1).Average(x => int.Parse(x)).ToString("F0")).ToArray());
                table.AddRow(gutsLine.Append(gutsLine.Skip(1).Average(x => int.Parse(x)).ToString("F0")).ToArray());
                table.AddRow(wizLine.Append(wizLine.Skip(1).Average(x => int.Parse(x)).ToString("F0")).ToArray());
                container.AddRow(table);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (Console.BufferWidth < 160 || Console.WindowWidth < 160))
            {
                Console.BufferWidth = 160;
                Console.SetWindowSize(Console.BufferWidth, Console.WindowHeight);
            }
            AnsiConsole.Write(container);

            static string GetProper(int proper) => proper switch
            {
                1 => "G",
                2 => "F",
                3 => "E",
                4 => "D",
                5 => "C",
                6 => "B",
                7 => "A",
                8 => "S",
                _ => "错误"
            };
        }
        static void ParsePracticeRaceRaceStartResponse(byte[] buffer) //练习赛
        {
            var @event = TryDeserialize<Gallop.PracticeRaceRaceStartResponse>(buffer);
            if (@event != default)
            {
                var data = @event.data;

                Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "races"));
                var lines = new List<string>
                {
                    $"Race Scenario:",
                    data.race_result_info.race_scenario,
                    string.Empty,
                    $"Race Horse Data Array",
                    JsonConvert.SerializeObject(data.race_result_info.race_horse_data_array),
                    string.Empty,
                    $"Trained Characters:"
                };
                foreach (var i in data.trained_chara_array)
                {
                    lines.Add(JsonConvert.SerializeObject(i, Formatting.None));
                    lines.Add(string.Empty);
                }
                File.WriteAllLines(@$"{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "races")}/{DateTime.Now:yy-MM-dd HH-mm-ss} PracticeRace.txt", lines);
            }
        }
        static void ParseRoomMatchRaceStartResponse(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.RoomMatchRaceStartResponse>(buffer);
            if (@event != default)
            {
                var data = @event.data;
                if (data.trained_chara_array == null) return;

                Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "races"));
                var lines = new List<string>
                {
                    $"Race Scenario:",
                    data.race_scenario,
                    string.Empty,
                    $"Race Horse Data Array",
                    JsonConvert.SerializeObject(data.race_horse_data_array),
                    string.Empty,
                    $"Trained Characters:"
                };
                foreach (var i in data.trained_chara_array)
                {
                    lines.Add(JsonConvert.SerializeObject(i, Formatting.None));
                    lines.Add(string.Empty);
                }
                File.WriteAllLines(@$"{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "races")}/{DateTime.Now:yy-MM-dd HH-mm-ss} RoomMatch.txt", lines);
            }
        }
        internal static T? TryDeserialize<T>(byte[] buffer)
        {
            try
            {
                return MessagePackSerializer.Deserialize<T>(buffer);
            }
            catch (Exception)
            {
                var json = MessagePackSerializer.ConvertToJson(buffer);
                return JsonConvert.DeserializeObject<T>(json);
            }
        }
    }
}
