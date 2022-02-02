using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

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

#if DEBUG
                    if (!Directory.Exists("response")) Directory.CreateDirectory("response");
                    File.WriteAllBytes(@$"response/{DateTime.Now.Ticks}.msgpack", buffer);
#endif
                    _ = Task.Run(() => ParseResponse(buffer));

                    await ctx.Response.OutputStream.WriteAsync(Array.Empty<byte>());
                    ctx.Response.Close();
                }
            });
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
                        case var CheckEvent when str.Contains("event_contents_info") && str.Contains("choice_array"):
                            ParseSingleModeCheckEventResponse(buffer);
                            break;
                        case var TrainedCharaLoad when str.Contains("trained_chara_array") && str.Contains("trained_chara_favorite_array") && str.Contains("room_match_entry_chara_id_array"):
                            ParseTrainedCharaLoadResponse(buffer);
                            break;
                        case var FriendSearch when str.Contains("friend_info") && str.Contains("user_info_summary") && str.Contains("practice_partner_info") && str.Contains("directory_card_array") && str.Contains("support_card_data") && str.Contains("release_num_info") && str.Contains("trophy_num_info") && str.Contains("team_stadium_user") && str.Contains("follower_num") && str.Contains("own_follow_num") && str.Contains("enable_circle_scout"):
                            ParseFriendSearchResponse(buffer);
                            break;
                        case var TeamStadiumOpponentList when str.Contains("opponent_info_array"):
                            ParseTeamStadiumOpponentListResponse(buffer.Replace(new byte[] { 0x88, 0xC0, 0x01 }, new byte[] { 0x87 }));
                            break;
                        default:
                            return;
                    }
                }
            }
            catch (Exception)
            {
#if DEBUG
                if (!Directory.Exists("response")) Directory.CreateDirectory("response");
                File.WriteAllBytes(@$"./response/{DateTime.Now.Ticks}.bin", buffer);
#endif
            }
        }
        static void ParseSingleModeCheckEventResponse(byte[] buffer)
        {
            var @event = MessagePack.MessagePackSerializer.Deserialize<Gallop.SingleModeCheckEventResponse>(buffer);
            if (@event != default && @event.data.unchecked_event_array?.Length > 0)
            {
                foreach (var i in @event.data.unchecked_event_array)
                {
                    if (i.event_contents_info?.choice_array.Length == 0) continue;
                    Console.WriteLine($"————————{Database.Events[i.story_id].Name} | {Database.Events[i.story_id].TriggerName}————————");
                    for (var j = 0; j < i.event_contents_info.choice_array.Length; ++j)
                    {
                        Console.WriteLine($"{Database.Events[i.story_id].Choices[j].Option} @ {i.event_contents_info.choice_array[j].select_index}");
                        Console.WriteLine($"{Database.Events[i.story_id].Choices[j].Effect}");
                        if ((j + 1) != i.event_contents_info.choice_array.Length)
                            Console.WriteLine("～～～～～～～～～～～～～～～～");
                    }
                    Console.WriteLine("—————————————————————————————————");
                }
            }
        }
        static void ParseTrainedCharaLoadResponse(byte[] buffer)
        {
            var @event = MessagePack.MessagePackSerializer.Deserialize<Gallop.TrainedCharaLoadResponse>(buffer);
            var data = @event.data;
            if (@event != default && data.trained_chara_array.Length > 0 && data.trained_chara_favorite_array.Length > 0)
            {
                var name = JObject.Parse("{\"100101\":\"[スペシャルドリーマー]スペシャルウィーク\",\"100102\":\"[ほっぴん♪ビタミンハート]スペシャルウィーク\",\"100201\":\"[サイレントイノセンス]サイレンススズカ\",\"100301\":\"[トップ・オブ・ジョイフル]トウカイテイオー\",\"100302\":\"[ビヨンド・ザ・ホライズン]トウカイテイオー\",\"100401\":\"[フォーミュラオブルージュ]マルゼンスキー\",\"100402\":\"[ぶっとび☆さまーナイト]マルゼンスキー\",\"100501\":\"[シューティンスタァ・ルヴュ]フジキセキ\",\"100601\":\"[スターライトビート]オグリキャップ\",\"100602\":\"[キセキの白星]オグリキャップ\",\"100701\":\"[レッドストライフ]ゴールドシップ\",\"100801\":\"[ワイルドトップギア]ウオッカ\",\"100901\":\"[トップ・オブ・ブルー]ダイワスカーレット\",\"101001\":\"[ワイルド・フロンティア]タイキシャトル\",\"101101\":\"[岩穿つ青]グラスワンダー\",\"101102\":\"[セイントジェード・ヒーラー]グラスワンダー\",\"101201\":\"[アマゾネス・ラピス]ヒシアマゾン\",\"101301\":\"[エレガンス・ライン]メジロマックイーン\",\"101302\":\"[エンド・オブ・スカイ]メジロマックイーン\",\"101401\":\"[エル☆Número 1]エルコンドルパサー\",\"101402\":\"[ククルカン・モンク]エルコンドルパサー\",\"101501\":\"[オー・ソレ・スーオ！]テイエムオペラオー\",\"101502\":\"[初晴・青き絢爛]テイエムオペラオー\",\"101601\":\"[Maverick]ナリタブライアン\",\"101701\":\"[ロード・オブ・エンペラー]シンボリルドルフ\",\"101702\":\"[皓月の弓取り]シンボリルドルフ\",\"101801\":\"[エンプレスロード]エアグルーヴ\",\"101802\":\"[クエルクス・キウィーリス]エアグルーヴ\",\"101901\":\"[超特急！フルカラー特殊PP]アグネスデジタル\",\"102001\":\"[あおぐもサミング]セイウンスカイ\",\"102101\":\"[疾風迅雷]タマモクロス\",\"102201\":\"[Noble Seamair]ファインモーション\",\"102301\":\"[pf.Victory formula...]ビワハヤヒデ\",\"102302\":\"[ノエルージュ・キャロル]ビワハヤヒデ\",\"102401\":\"[すくらんぶる☆ゾーン]マヤノトップガン\",\"102402\":\"[サンライト・ブーケ]マヤノトップガン\",\"102501\":\"[Creeping Black]マンハッタンカフェ\",\"102601\":\"[MB-19890425]ミホノブルボン\",\"102602\":\"[CODE：グラサージュ]ミホノブルボン\",\"102701\":\"[ストレート・ライン]メジロライアン\",\"102801\":\"[ボーノ☆アラモーダ]ヒシアケボノ\",\"103001\":\"[ローゼスドリーム]ライスシャワー\",\"103002\":\"[Make up Vampire!]ライスシャワー\",\"103201\":\"[tach-nology]アグネスタキオン\",\"103501\":\"[Go To Winning!]ウイニングチケット\",\"103701\":\"[Meisterschaft]エイシンフラッシュ\",\"103702\":\"[コレクト・ショコラティエ]エイシンフラッシュ\",\"103801\":\"[フィーユ・エクレール]カレンチャン\",\"103901\":\"[プリンセス・オブ・ピンク]カワカミプリンセス\",\"104001\":\"[オーセンティック/1928]ゴールドシチー\",\"104002\":\"[秋桜ダンツァトリーチェ]ゴールドシチー\",\"104101\":\"[サクラ、すすめ！]サクラバクシンオー\",\"104501\":\"[マーマリングストリーム]スーパークリーク\",\"104502\":\"[シフォンリボンマミー]スーパークリーク\",\"104601\":\"[あぶそりゅーと☆LOVE]スマートファルコン\",\"104801\":\"[ポップス☆ジョーカー]トーセンジョーダン\",\"105001\":\"[Nevertheless]ナリタタイシン\",\"105201\":\"[うららん一等賞♪]ハルウララ\",\"105202\":\"[初うらら♪さくさくら]ハルウララ\",\"105601\":\"[運気上昇☆幸福万来]マチカネフクキタル\",\"105602\":\"[吉兆・初あらし]マチカネフクキタル\",\"105801\":\"[ブルー/レイジング]メイショウドトウ\",\"105901\":\"[ツイステッド・ライン]メジロドーベル\",\"106001\":\"[ポインセチア・リボン]ナイスネイチャ\",\"106101\":\"[キング・オブ・エメラルド]キングヘイロー\",\"106901\":\"[日下開山・花あかり]サクラチヨノオー\"}").ToObject<Dictionary<int, string>>();
                { //自己的马
                    var fav_ids = data.trained_chara_favorite_array.Select(x => x.trained_chara_id).ToList();
                    var chara = data.trained_chara_array.Where(x => fav_ids.Contains(x.trained_chara_id));
                    var win_saddle_result = new List<(string Name, int WinSaddle, int Score)>();
                    foreach (var i in chara)
                        win_saddle_result.Add((name[i.card_id], i.win_saddle_id_array.Intersect(i.succession_chara_array[0].win_saddle_id_array).Count()
                        + i.win_saddle_id_array.Intersect(i.succession_chara_array[1].win_saddle_id_array).Count(), i.rank_score));
                    win_saddle_result.Sort((a, b) => b.WinSaddle.CompareTo(a.WinSaddle));
                    foreach (var (Name, WinSaddle, Score) in win_saddle_result)
                        Console.WriteLine($"{Name}\t\t{WinSaddle}\t\t{Score}");
                }
            }
        }
        static void ParseFriendSearchResponse(byte[] buffer)
        {
            var @event = MessagePack.MessagePackSerializer.Deserialize<Gallop.FriendSearchResponse>(buffer);
            var data = @event.data;
            if (@event != default)
            {
                var name = JObject.Parse("{\"100101\":\"[スペシャルドリーマー]スペシャルウィーク\",\"100102\":\"[ほっぴん♪ビタミンハート]スペシャルウィーク\",\"100201\":\"[サイレントイノセンス]サイレンススズカ\",\"100301\":\"[トップ・オブ・ジョイフル]トウカイテイオー\",\"100302\":\"[ビヨンド・ザ・ホライズン]トウカイテイオー\",\"100401\":\"[フォーミュラオブルージュ]マルゼンスキー\",\"100402\":\"[ぶっとび☆さまーナイト]マルゼンスキー\",\"100501\":\"[シューティンスタァ・ルヴュ]フジキセキ\",\"100601\":\"[スターライトビート]オグリキャップ\",\"100602\":\"[キセキの白星]オグリキャップ\",\"100701\":\"[レッドストライフ]ゴールドシップ\",\"100801\":\"[ワイルドトップギア]ウオッカ\",\"100901\":\"[トップ・オブ・ブルー]ダイワスカーレット\",\"101001\":\"[ワイルド・フロンティア]タイキシャトル\",\"101101\":\"[岩穿つ青]グラスワンダー\",\"101102\":\"[セイントジェード・ヒーラー]グラスワンダー\",\"101201\":\"[アマゾネス・ラピス]ヒシアマゾン\",\"101301\":\"[エレガンス・ライン]メジロマックイーン\",\"101302\":\"[エンド・オブ・スカイ]メジロマックイーン\",\"101401\":\"[エル☆Número 1]エルコンドルパサー\",\"101402\":\"[ククルカン・モンク]エルコンドルパサー\",\"101501\":\"[オー・ソレ・スーオ！]テイエムオペラオー\",\"101502\":\"[初晴・青き絢爛]テイエムオペラオー\",\"101601\":\"[Maverick]ナリタブライアン\",\"101701\":\"[ロード・オブ・エンペラー]シンボリルドルフ\",\"101702\":\"[皓月の弓取り]シンボリルドルフ\",\"101801\":\"[エンプレスロード]エアグルーヴ\",\"101802\":\"[クエルクス・キウィーリス]エアグルーヴ\",\"101901\":\"[超特急！フルカラー特殊PP]アグネスデジタル\",\"102001\":\"[あおぐもサミング]セイウンスカイ\",\"102101\":\"[疾風迅雷]タマモクロス\",\"102201\":\"[Noble Seamair]ファインモーション\",\"102301\":\"[pf.Victory formula...]ビワハヤヒデ\",\"102302\":\"[ノエルージュ・キャロル]ビワハヤヒデ\",\"102401\":\"[すくらんぶる☆ゾーン]マヤノトップガン\",\"102402\":\"[サンライト・ブーケ]マヤノトップガン\",\"102501\":\"[Creeping Black]マンハッタンカフェ\",\"102601\":\"[MB-19890425]ミホノブルボン\",\"102602\":\"[CODE：グラサージュ]ミホノブルボン\",\"102701\":\"[ストレート・ライン]メジロライアン\",\"102801\":\"[ボーノ☆アラモーダ]ヒシアケボノ\",\"103001\":\"[ローゼスドリーム]ライスシャワー\",\"103002\":\"[Make up Vampire!]ライスシャワー\",\"103201\":\"[tach-nology]アグネスタキオン\",\"103501\":\"[Go To Winning!]ウイニングチケット\",\"103701\":\"[Meisterschaft]エイシンフラッシュ\",\"103702\":\"[コレクト・ショコラティエ]エイシンフラッシュ\",\"103801\":\"[フィーユ・エクレール]カレンチャン\",\"103901\":\"[プリンセス・オブ・ピンク]カワカミプリンセス\",\"104001\":\"[オーセンティック/1928]ゴールドシチー\",\"104002\":\"[秋桜ダンツァトリーチェ]ゴールドシチー\",\"104101\":\"[サクラ、すすめ！]サクラバクシンオー\",\"104501\":\"[マーマリングストリーム]スーパークリーク\",\"104502\":\"[シフォンリボンマミー]スーパークリーク\",\"104601\":\"[あぶそりゅーと☆LOVE]スマートファルコン\",\"104801\":\"[ポップス☆ジョーカー]トーセンジョーダン\",\"105001\":\"[Nevertheless]ナリタタイシン\",\"105201\":\"[うららん一等賞♪]ハルウララ\",\"105202\":\"[初うらら♪さくさくら]ハルウララ\",\"105601\":\"[運気上昇☆幸福万来]マチカネフクキタル\",\"105602\":\"[吉兆・初あらし]マチカネフクキタル\",\"105801\":\"[ブルー/レイジング]メイショウドトウ\",\"105901\":\"[ツイステッド・ライン]メジロドーベル\",\"106001\":\"[ポインセチア・リボン]ナイスネイチャ\",\"106101\":\"[キング・オブ・エメラルド]キングヘイロー\",\"106901\":\"[日下開山・花あかり]サクラチヨノオー\"}").ToObject<Dictionary<int, string>>();
                //var i = ParseWinSaddle(jo["data"]["practice_partner_info"]);
                var i = data.practice_partner_info;
                var (Name, WinSaddle, Score) = (name[i.card_id], i.win_saddle_id_array.Intersect(i.succession_chara_array[0].win_saddle_id_array).Count()
                        + i.win_saddle_id_array.Intersect(i.succession_chara_array[1].win_saddle_id_array).Count(), i.rank_score);
                Console.WriteLine("—————————————————————————————————");
                Console.WriteLine($"好友：{data.user_info_summary.name}\t\tFollower数：{data.follower_num}");
                Console.WriteLine($"种马：{Name}\t\t{WinSaddle}\t\t{Score}");
                Console.WriteLine("—————————————————————————————————");
            }
        }
        static void ParseTeamStadiumOpponentListResponse(byte[] buffer)
        {
            var @event = MessagePack.MessagePackSerializer.Deserialize<Gallop.TeamStadiumOpponentListResponse>(buffer);
            var data = @event.data;
            foreach (var i in data.opponent_info_array.OrderByDescending(x => x.strength))
            {
                var Type = i.strength switch
                {
                    1 => "上",
                    2 => "中",
                    3 => "下"
                };
                Console.WriteLine($"————————————————{Type}————————————————");
                var dic = new Dictionary<int, string>();
                foreach (var j in i.trained_chara_array)
                    dic.Add(j.trained_chara_id, $"\t短{GetProper(j.proper_distance_short)}\t英{GetProper(j.proper_distance_mile)}\t中{GetProper(j.proper_distance_middle)}\t长{GetProper(j.proper_distance_long)}\t泥{GetProper(j.proper_ground_dirt)}{Environment.NewLine}\t速{j.speed}\t耐{j.stamina}\t力{j.power}\t根{j.guts}\t智{j.wiz}");
                foreach (var j in i.team_data_array.GroupBy(x => x.distance_type))
                {
                    var distance = j.Key switch
                    {
                        1 => "短",
                        2 => "英",
                        3 => "中",
                        4 => "长",
                        5 => "泥"
                    };
                    Console.WriteLine($"{distance}: ");
                    foreach (var k in j)
                        Console.WriteLine(dic[k.trained_chara_id]);
                }
            }

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
    }
}
