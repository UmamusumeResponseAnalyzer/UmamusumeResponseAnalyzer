using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Net;
using System.Runtime.InteropServices;
using UmamusumeResponseAnalyzer.Localization;
using UmamusumeResponseAnalyzer.Handler;

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
                    if (str.Contains("chara_info") && str.Contains("home_info") && str.Contains("command_info_array") && !str.Contains("race_reward_info")) //根据文本简单过滤防止重复、异常输出
                    {
                        if (Config.Get(Resource.ConfigSet_ShowCommandInfo))
                            Handlers.ParseCommandInfo(buffer);
                    }
                    if ((str.Contains("chara_info") && str.Contains("race_condition_array")) || str.Contains("unchecked_event_array"))
                    {
                        if (Config.Get(Resource.ConfigSet_ParseSingleModeCheckEventResponse))
                            Handlers.ParseSingleModeCheckEventResponse(buffer);
                        if (Config.Get(Resource.ConfigSet_MaximiumGradeSkillRecommendation) && str.Contains("skill_tips_array"))
                            Handlers.ParseSkillTipsResponse(buffer);
                    }
                    if (str.Contains("trained_chara_array") && str.Contains("trained_chara_favorite_array") && str.Contains("room_match_entry_chara_id_array"))
                    {
                        if (Config.Get(Resource.ConfigSet_ParseTrainedCharaLoadResponse))
                            Handlers.ParseTrainedCharaLoadResponse(buffer);
                    }
                    if (str.Contains("friend_info") && str.Contains("user_info_summary") && str.Contains("practice_partner_info") && str.Contains("directory_card_array") && str.Contains("support_card_data") && str.Contains("release_num_info") && str.Contains("trophy_num_info") && str.Contains("team_stadium_user") && str.Contains("follower_num") && str.Contains("own_follow_num") && str.Contains("enable_circle_scout"))
                    {
                        if (Config.Get(Resource.ConfigSet_ParseFriendSearchResponse))
                            Handlers.ParseFriendSearchResponse(buffer);
                    }
                    if (str.Contains("opponent_info_array"))
                    {
                        if (Config.Get(Resource.ConfigSet_ParseTeamStadiumOpponentListResponse))
                            // https://github.com/CNA-Bld/EXNOA-CarrotJuicer/issues/2
                            Handlers.ParseTeamStadiumOpponentListResponse(buffer.Replace(new byte[] { 0x88, 0xC0, 0x01 }, new byte[] { 0x87 }));
                    }
                    if (str.Contains("trained_chara_array") && str.Contains("race_result_info") && str.Contains("entry_info_array") && str.Contains("practice_race_id") && str.Contains("state") && str.Contains("practice_partner_owner_info_array"))
                    {
                        if (Config.Get(Resource.ConfigSet_ParsePracticeRaceRaceStartResponse))
                            Handlers.ParsePracticeRaceRaceStartResponse(buffer);
                    }
                    if (str.Contains("race_scenario") && str.Contains("random_seed") && str.Contains("race_horse_data_array") && str.Contains("trained_chara_array") && str.Contains("season") && str.Contains("weather") && str.Contains("ground_condition"))
                    {
                        if (Config.Get(Resource.ConfigSet_ParseRoomMatchRaceStartResponse))
                            Handlers.ParseRoomMatchRaceStartResponse(buffer);
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
