using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Net;
using UmamusumeResponseAnalyzer.Localization;

namespace UmamusumeResponseAnalyzer
{
    internal static class Server
    {
        static readonly HttpListener httpListener = new();
        static object _lock = new object();
        public static void Start()
        {
            httpListener.Prefixes.Add("http://127.0.0.1:4693/");
            httpListener.Start();
            Task.Run(async () =>
            {
                while (true)
                {
                    var ctx = await httpListener.GetContextAsync();

                    var ms = new MemoryStream();
                    ctx.Request.InputStream.CopyTo(ms);
                    var buffer = ms.ToArray();

                    if (ctx.Request.RawUrl == "/notify/response")
                    {
#if DEBUG
                        if (!Directory.Exists("response")) Directory.CreateDirectory("response");
                        var tick = DateTime.Now.Ticks;
                        File.WriteAllBytes(@$"response/{tick}.msgpack", buffer);
                        File.WriteAllText(@$"response/{tick}.json", JObject.Parse(MessagePack.MessagePackSerializer.ConvertToJson(buffer)).ToString());
#endif
                        _ = Task.Run(() => ParseResponse(buffer));
                    }
                    else if (ctx.Request.RawUrl == "/notify/request")
                    {
                        var msgpack = buffer[170..];
                        File.WriteAllBytes(@$"request/{DateTime.Now.Ticks}.msgpack", msgpack);

                        _ = Task.Run(() => ParseRequest(msgpack));
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
                    var str = MessagePack.MessagePackSerializer.ConvertToJson(buffer);
                    switch (str)
                    {
                        case var SingleModeExecCommand when str.Contains("command_type") && str.Contains("command_id") && str.Contains("command_group_id"):
                            //if (Config.Get(Resource.ConfigSet_ParseSingleModeExecCommandRequest))
                            ParseSingleModeExecCommandRequest(buffer);
                            break;
                        case var RaceAnalyze when str.Contains("program_id") && str.Contains("current_turn"):
                            //if (Config.Get(Resource.ConfigSet_ParseTrainedCharaLoadResponse))
                            ParseRaceAnalyzeRequest(buffer);
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
        static void ParseResponse(byte[] buffer)
        {
            try
            {
                lock (_lock)
                {
                    var str = MessagePack.MessagePackSerializer.ConvertToJson(buffer);
                    switch (str)
                    {
                        case var CheckEvent when (str.Contains("chara_info") && str.Contains("race_condition_array")) || str.Contains("unchecked_event_array"):
                            if (Config.Get(Resource.ConfigSet_ParseSingleModeCheckEventResponse))
                                ParseSingleModeCheckEventResponse(buffer);
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
                        default:
                            return;
                    }
                }
            }
            catch (Exception)
            {
            }
        }
        static void ParseSingleModeExecCommandRequest(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.SingleModeExecCommandRequest>(buffer);
            if (@event != default)
            {
                var (Year, MDays) = Math.DivRem(@event.current_turn + 1, 24);
                if (MDays == 0) MDays = 24;
                var (Month, Days) = Math.DivRem(MDays + 1, 2);
                var date = string.Format(Resource.Events_ParseSingleModeCheckEventResponse_Date, MDays == 24 ? Year : (Year + 1), Month, Days == 1 ? Resource.Events_ParseSingleModeCheckEventResponse_Date_Lower : Resource.Events_ParseSingleModeCheckEventResponse_Date_Upper);
                var dateCode = $"{(MDays == 24 ? Year : Year + 1)}{(Month < 10 ? $"0{Month}" : Month)}{Days + 1}";
                var race = Config.Get<List<string>>("Races").FirstOrDefault(x => x[..4] == dateCode);
                if (race != default && Database.Races.ContainsKey(race)) race = Database.Races[race];
                var rule = new Rule(string.Format(Resource.Events_NextTurnPrompting, date, race != default ? string.Format(Resource.Events_NextTurnRacePrompting, race) : string.Empty))
                    .Alignment(Justify.Left);
                AnsiConsole.Write(rule);
            }
        }
        static void ParseRaceAnalyzeRequest(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.RaceAnalyzeRequest>(buffer);
            if (@event != default)
            {
                var (Year, MDays) = Math.DivRem(@event.current_turn + 1, 24);
                if (MDays == 0) MDays = 24;
                var (Month, Days) = Math.DivRem(MDays + 1, 2);
                var date = string.Format(Resource.Events_ParseSingleModeCheckEventResponse_Date, MDays == 24 ? Year : (Year + 1), Month, Days == 1 ? Resource.Events_ParseSingleModeCheckEventResponse_Date_Lower : Resource.Events_ParseSingleModeCheckEventResponse_Date_Upper);
                var dateCode = $"{(MDays == 24 ? Year : Year + 1)}{(Month < 10 ? $"0{Month}" : Month)}{Days + 1}";
                var race = Config.Get<List<string>>("Races").FirstOrDefault(x => x[..4] == dateCode);
                if (race != default && Database.Races.ContainsKey(race)) race = Database.Races[race];
                var rule = new Rule(string.Format(Resource.Events_NextTurnPrompting, date, race != default ? string.Format(Resource.Events_NextTurnRacePrompting, race) : string.Empty))
                    .Alignment(Justify.Left);
                AnsiConsole.Write(rule);
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
                        if (i.event_contents_info?.choice_array.Length == 0) continue;
                        var mainTree = new Tree(Database.Events[i.story_id].TriggerName.EscapeMarkup());
                        var eventTree = new Tree(Database.Events[i.story_id].Name.EscapeMarkup());
                        var success = Database.SuccessEvent.TryGetValue(Database.Events[i.story_id].Name, out var successEvent);
                        for (var j = 0; j < i.event_contents_info.choice_array.Length; ++j)
                        {
                            var tree = new Tree($"{Database.Events[i.story_id].Choices[j].Option} @ {i.event_contents_info.choice_array[j].select_index}".EscapeMarkup());
                            if (success)
                            {
                                var successChoice = successEvent.Choices.FirstOrDefault(x => x.ChoiceIndex == j + 1);
                                if (successChoice != default)
                                {
                                    if (successChoice.SelectIndex == i.event_contents_info.choice_array[j].select_index)
                                        tree.AddNode($"[mediumspringgreen on #081129]{successChoice.Effect.EscapeMarkup()}[/]");
                                    else
                                        tree.AddNode($"[#FF0050 on #081129]{Database.Events[i.story_id].Choices[j].Effect.EscapeMarkup()}[/]");
                                }
                            }
                            else
                            {
                                tree.AddNode($"{Database.Events[i.story_id].Choices[j].Effect}".EscapeMarkup());
                            }
                            eventTree.AddNode(tree);
                        }
                        mainTree.AddNode(eventTree);
                        AnsiConsole.Write(mainTree);
                    }
                }
            }
        }
        static void ParseTrainedCharaLoadResponse(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.TrainedCharaLoadResponse>(buffer);
            var data = @event.data;
            if (@event != default && data.trained_chara_array.Length > 0 && data.trained_chara_favorite_array.Length > 0)
            {
                var name = JObject.Parse("{\"100101\":\"[スペシャルドリーマー]スペシャルウィーク\",\"100102\":\"[ほっぴん♪ビタミンハート]スペシャルウィーク\",\"100201\":\"[サイレントイノセンス]サイレンススズカ\",\"100301\":\"[トップ・オブ・ジョイフル]トウカイテイオー\",\"100302\":\"[ビヨンド・ザ・ホライズン]トウカイテイオー\",\"100401\":\"[フォーミュラオブルージュ]マルゼンスキー\",\"100402\":\"[ぶっとび☆さまーナイト]マルゼンスキー\",\"100501\":\"[シューティンスタァ・ルヴュ]フジキセキ\",\"100601\":\"[スターライトビート]オグリキャップ\",\"100602\":\"[キセキの白星]オグリキャップ\",\"100701\":\"[レッドストライフ]ゴールドシップ\",\"100801\":\"[ワイルドトップギア]ウオッカ\",\"100901\":\"[トップ・オブ・ブルー]ダイワスカーレット\",\"101001\":\"[ワイルド・フロンティア]タイキシャトル\",\"101101\":\"[岩穿つ青]グラスワンダー\",\"101102\":\"[セイントジェード・ヒーラー]グラスワンダー\",\"101201\":\"[アマゾネス・ラピス]ヒシアマゾン\",\"101301\":\"[エレガンス・ライン]メジロマックイーン\",\"101302\":\"[エンド・オブ・スカイ]メジロマックイーン\",\"101401\":\"[エル☆Número 1]エルコンドルパサー\",\"101402\":\"[ククルカン・モンク]エルコンドルパサー\",\"101501\":\"[オー・ソレ・スーオ！]テイエムオペラオー\",\"101502\":\"[初晴・青き絢爛]テイエムオペラオー\",\"101601\":\"[Maverick]ナリタブライアン\",\"101701\":\"[ロード・オブ・エンペラー]シンボリルドルフ\",\"101702\":\"[皓月の弓取り]シンボリルドルフ\",\"101801\":\"[エンプレスロード]エアグルーヴ\",\"101802\":\"[クエルクス・キウィーリス]エアグルーヴ\",\"101901\":\"[超特急！フルカラー特殊PP]アグネスデジタル\",\"102001\":\"[あおぐもサミング]セイウンスカイ\",\"102101\":\"[疾風迅雷]タマモクロス\",\"102201\":\"[Noble Seamair]ファインモーション\",\"102301\":\"[pf.Victory formula...]ビワハヤヒデ\",\"102302\":\"[ノエルージュ・キャロル]ビワハヤヒデ\",\"102401\":\"[すくらんぶる☆ゾーン]マヤノトップガン\",\"102402\":\"[サンライト・ブーケ]マヤノトップガン\",\"102501\":\"[Creeping Black]マンハッタンカフェ\",\"102601\":\"[MB-19890425]ミホノブルボン\",\"102602\":\"[CODE：グラサージュ]ミホノブルボン\",\"102701\":\"[ストレート・ライン]メジロライアン\",\"102801\":\"[ボーノ☆アラモーダ]ヒシアケボノ\",\"103001\":\"[ローゼスドリーム]ライスシャワー\",\"103002\":\"[Make up Vampire!]ライスシャワー\",\"103201\":\"[tach-nology]アグネスタキオン\",\"103501\":\"[Go To Winning!]ウイニングチケット\",\"103701\":\"[Meisterschaft]エイシンフラッシュ\",\"103702\":\"[コレクト・ショコラティエ]エイシンフラッシュ\",\"103801\":\"[フィーユ・エクレール]カレンチャン\",\"103901\":\"[プリンセス・オブ・ピンク]カワカミプリンセス\",\"104001\":\"[オーセンティック/1928]ゴールドシチー\",\"104002\":\"[秋桜ダンツァトリーチェ]ゴールドシチー\",\"104101\":\"[サクラ、すすめ！]サクラバクシンオー\",\"104501\":\"[マーマリングストリーム]スーパークリーク\",\"104502\":\"[シフォンリボンマミー]スーパークリーク\",\"104601\":\"[あぶそりゅーと☆LOVE]スマートファルコン\",\"104801\":\"[ポップス☆ジョーカー]トーセンジョーダン\",\"105001\":\"[Nevertheless]ナリタタイシン\",\"105201\":\"[うららん一等賞♪]ハルウララ\",\"105202\":\"[初うらら♪さくさくら]ハルウララ\",\"105601\":\"[運気上昇☆幸福万来]マチカネフクキタル\",\"105602\":\"[吉兆・初あらし]マチカネフクキタル\",\"105801\":\"[ブルー/レイジング]メイショウドトウ\",\"105901\":\"[ツイステッド・ライン]メジロドーベル\",\"106001\":\"[ポインセチア・リボン]ナイスネイチャ\",\"106101\":\"[キング・オブ・エメラルド]キングヘイロー\",\"106901\":\"[日下開山・花あかり]サクラチヨノオー\"}").ToObject<Dictionary<int, string>>();
                var fav_ids = data.trained_chara_favorite_array.Select(x => x.trained_chara_id).ToList();
                var chara = data.trained_chara_array.Where(x => fav_ids.Contains(x.trained_chara_id));
                var win_saddle_result = new List<(string Name, int WinSaddle, int Score)>();
                foreach (var i in chara)
                    win_saddle_result.Add((name[i.card_id], i.win_saddle_id_array.Intersect(i.succession_chara_array[0].win_saddle_id_array).Count()
                    + i.win_saddle_id_array.Intersect(i.succession_chara_array[1].win_saddle_id_array).Count(), i.rank_score));
                win_saddle_result.Sort((a, b) => b.WinSaddle.CompareTo(a.WinSaddle));
                var table = new Table();
                table.Border = TableBorder.Ascii;
                table.AddColumns("种马名", "胜鞍加成", "分数");
                foreach (var (Name, WinSaddle, Score) in win_saddle_result)
                    table.AddRow(Name.EscapeMarkup(), WinSaddle.ToString(), Score.ToString());
                AnsiConsole.Write(table);
            }
        }
        static void ParseFriendSearchResponse(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.FriendSearchResponse>(buffer);
            var data = @event.data;
            if (@event != default)
            {
                var name = JObject.Parse("{\"100101\":\"[スペシャルドリーマー]スペシャルウィーク\",\"100102\":\"[ほっぴん♪ビタミンハート]スペシャルウィーク\",\"100201\":\"[サイレントイノセンス]サイレンススズカ\",\"100301\":\"[トップ・オブ・ジョイフル]トウカイテイオー\",\"100302\":\"[ビヨンド・ザ・ホライズン]トウカイテイオー\",\"100401\":\"[フォーミュラオブルージュ]マルゼンスキー\",\"100402\":\"[ぶっとび☆さまーナイト]マルゼンスキー\",\"100501\":\"[シューティンスタァ・ルヴュ]フジキセキ\",\"100601\":\"[スターライトビート]オグリキャップ\",\"100602\":\"[キセキの白星]オグリキャップ\",\"100701\":\"[レッドストライフ]ゴールドシップ\",\"100801\":\"[ワイルドトップギア]ウオッカ\",\"100901\":\"[トップ・オブ・ブルー]ダイワスカーレット\",\"101001\":\"[ワイルド・フロンティア]タイキシャトル\",\"101101\":\"[岩穿つ青]グラスワンダー\",\"101102\":\"[セイントジェード・ヒーラー]グラスワンダー\",\"101201\":\"[アマゾネス・ラピス]ヒシアマゾン\",\"101301\":\"[エレガンス・ライン]メジロマックイーン\",\"101302\":\"[エンド・オブ・スカイ]メジロマックイーン\",\"101401\":\"[エル☆Número 1]エルコンドルパサー\",\"101402\":\"[ククルカン・モンク]エルコンドルパサー\",\"101501\":\"[オー・ソレ・スーオ！]テイエムオペラオー\",\"101502\":\"[初晴・青き絢爛]テイエムオペラオー\",\"101601\":\"[Maverick]ナリタブライアン\",\"101701\":\"[ロード・オブ・エンペラー]シンボリルドルフ\",\"101702\":\"[皓月の弓取り]シンボリルドルフ\",\"101801\":\"[エンプレスロード]エアグルーヴ\",\"101802\":\"[クエルクス・キウィーリス]エアグルーヴ\",\"101901\":\"[超特急！フルカラー特殊PP]アグネスデジタル\",\"102001\":\"[あおぐもサミング]セイウンスカイ\",\"102101\":\"[疾風迅雷]タマモクロス\",\"102201\":\"[Noble Seamair]ファインモーション\",\"102301\":\"[pf.Victory formula...]ビワハヤヒデ\",\"102302\":\"[ノエルージュ・キャロル]ビワハヤヒデ\",\"102401\":\"[すくらんぶる☆ゾーン]マヤノトップガン\",\"102402\":\"[サンライト・ブーケ]マヤノトップガン\",\"102501\":\"[Creeping Black]マンハッタンカフェ\",\"102601\":\"[MB-19890425]ミホノブルボン\",\"102602\":\"[CODE：グラサージュ]ミホノブルボン\",\"102701\":\"[ストレート・ライン]メジロライアン\",\"102801\":\"[ボーノ☆アラモーダ]ヒシアケボノ\",\"103001\":\"[ローゼスドリーム]ライスシャワー\",\"103002\":\"[Make up Vampire!]ライスシャワー\",\"103201\":\"[tach-nology]アグネスタキオン\",\"103501\":\"[Go To Winning!]ウイニングチケット\",\"103701\":\"[Meisterschaft]エイシンフラッシュ\",\"103702\":\"[コレクト・ショコラティエ]エイシンフラッシュ\",\"103801\":\"[フィーユ・エクレール]カレンチャン\",\"103901\":\"[プリンセス・オブ・ピンク]カワカミプリンセス\",\"104001\":\"[オーセンティック/1928]ゴールドシチー\",\"104002\":\"[秋桜ダンツァトリーチェ]ゴールドシチー\",\"104101\":\"[サクラ、すすめ！]サクラバクシンオー\",\"104501\":\"[マーマリングストリーム]スーパークリーク\",\"104502\":\"[シフォンリボンマミー]スーパークリーク\",\"104601\":\"[あぶそりゅーと☆LOVE]スマートファルコン\",\"104801\":\"[ポップス☆ジョーカー]トーセンジョーダン\",\"105001\":\"[Nevertheless]ナリタタイシン\",\"105201\":\"[うららん一等賞♪]ハルウララ\",\"105202\":\"[初うらら♪さくさくら]ハルウララ\",\"105601\":\"[運気上昇☆幸福万来]マチカネフクキタル\",\"105602\":\"[吉兆・初あらし]マチカネフクキタル\",\"105801\":\"[ブルー/レイジング]メイショウドトウ\",\"105901\":\"[ツイステッド・ライン]メジロドーベル\",\"106001\":\"[ポインセチア・リボン]ナイスネイチャ\",\"106101\":\"[キング・オブ・エメラルド]キングヘイロー\",\"106901\":\"[日下開山・花あかり]サクラチヨノオー\"}").ToObject<Dictionary<int, string>>();
                var i = data.practice_partner_info;
                var (Name, WinSaddle, Score) = (name[i.card_id], i.win_saddle_id_array.Intersect(i.succession_chara_array[0].win_saddle_id_array).Count()
                        + i.win_saddle_id_array.Intersect(i.succession_chara_array[1].win_saddle_id_array).Count(), i.rank_score);
                AnsiConsole.Write(new Rule());
                AnsiConsole.WriteLine($"好友：{data.user_info_summary.name}\t\tFollower数：{data.follower_num}");
                AnsiConsole.WriteLine($"种马：{Name}\t\t{WinSaddle}\t\t{Score}");
                AnsiConsole.Write(new Rule());
            }
        }
        static void ParseTeamStadiumOpponentListResponse(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.TeamStadiumOpponentListResponse>(buffer);
            var data = @event.data;
            var container = new Table();
            container.Border = TableBorder.Double;
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
            if (Console.BufferWidth < 160 || Console.WindowWidth < 160)
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
        internal static T TryDeserialize<T>(byte[] buffer)
        {
            try
            {
                return MessagePack.MessagePackSerializer.Deserialize<T>(buffer);
            }
            catch (Exception)
            {
                var json = MessagePack.MessagePackSerializer.ConvertToJson(buffer);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
            }
        }
    }
}
