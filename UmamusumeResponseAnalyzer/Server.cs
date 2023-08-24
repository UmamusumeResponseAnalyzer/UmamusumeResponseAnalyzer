using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Net;
using System.Runtime.InteropServices;
using UmamusumeResponseAnalyzer.Localization;
using UmamusumeResponseAnalyzer.Handler;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer
{
    internal static class Server
    {
        static HttpListener httpListener;
        static readonly object _lock = new();
        static Mutex Mutex;
        public static ManualResetEvent OnPing = new(false);
        public static void Start()
        {
            try
            {
                httpListener = new();
                httpListener.Prefixes.Add("http://*:4693/");
                httpListener.Start();
                AnsiConsole.MarkupLine("服务器已于http://*:4693启动");
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                   .Where(x => x.NetworkInterfaceType == NetworkInterfaceType.Ethernet && x.OperationalStatus == OperationalStatus.Up)
                   .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                   .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                   .Select(x => x.Address.ToString())
                   .ToList();
                foreach (var i in interfaces)
                {
                    AnsiConsole.WriteLine($"可尝试通过http://{i}:4693连接");
                }
            }
            catch
            {
                try
                {
                    httpListener = new();
                    httpListener.Prefixes.Add("http://127.0.0.1:4693/");
                    httpListener.Start();
                    AnsiConsole.MarkupLine("服务器已于http://127.0.0.1:4693启动，如需模拟器/手机连入请以管理员权限运行");
                }
                catch (HttpListenerException)
                {
                    AnsiConsole.WriteLine("服务器启动失败，请检查是否已有URA实例正在运行");
                }
            }
            finally
            {
                Mutex = new Mutex(true, "UmamusumeResponseAnalyzerMutex");
            }
            Task.Run(async () =>
            {
                while (httpListener.IsListening)
                {
                    try
                    {
                        var ctx = await httpListener.GetContextAsync();

                        using var ms = new MemoryStream();
                        ctx.Request.InputStream.CopyTo(ms);
                        var buffer = ms.ToArray();

                        if (ctx.Request.RawUrl == "/notify/response")
                        {
                            Directory.CreateDirectory("packets");


#if DEBUG

                            File.WriteAllBytes($@"./packets/{DateTime.Now:yy-MM-dd HH-mm-ss-fff}R.bin", buffer);
                            File.WriteAllText($@"./packets/Turn{GameStats.currentTurn}_{DateTime.Now:yy-MM-dd HH-mm-ss-fff}R.json", JObject.Parse(MessagePackSerializer.ConvertToJson(buffer)).ToString());
#endif
                            if (Config.Get(Resource.ConfigSet_SaveResponseForDebug))
                            {
                                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "packets");
                                if (Directory.Exists(directory))
                                {
                                    foreach (var i in Directory.GetFiles(directory))
                                    {
                                        var fileInfo = new FileInfo(i);
                                        if (fileInfo.CreationTime.AddDays(1) < DateTime.Now)
                                            fileInfo.Delete();
                                    }
                                }
                                else
                                {
                                    Directory.CreateDirectory(directory);
                                }
                                File.WriteAllBytes($"{directory}/{DateTime.Now:yy-MM-dd HH-mm-ss-fff}R.msgpack", buffer);
                            }
                            _ = Task.Run(() => ParseResponse(buffer));
                        }
                        else if (ctx.Request.RawUrl == "/notify/request")
                        {
                            Directory.CreateDirectory("packets");
#if DEBUG
                            File.WriteAllText($@"./packets/Turn{GameStats.currentTurn}_{DateTime.Now:yy-MM-dd HH-mm-ss-fff}Q.json", JObject.Parse(MessagePackSerializer.ConvertToJson(buffer.AsMemory()[170..])).ToString());

#endif
                            _ = Task.Run(() => ParseRequest(buffer[170..]));
                        }
                        else if (ctx.Request.RawUrl == "/notify/ping")
                        {
                            AnsiConsole.MarkupLine("[green]检测到从游戏发来的请求，配置正确[/]");
                            await ctx.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("pong"));
                            ctx.Response.Close();
                            OnPing.Signal();
                            continue;
                        }

                        await ctx.Response.OutputStream.WriteAsync(Array.Empty<byte>());
                        ctx.Response.Close();
                    }
                    catch
                    {

                    }
                }
            });
        }
        public static bool IsRunning => httpListener.IsListening;
        static void ParseRequest(byte[] buffer)
        {
            try
            {
                lock (_lock)
                {
                    var str = MessagePackSerializer.ConvertToJson(buffer);
                    //AnsiConsole.WriteLine(str);
                    var dyn = JsonConvert.DeserializeObject<dynamic>(str);

                    if (dyn == default(dynamic)) return;

                    if (dyn.command_type==1) //玩家点击了训练
                    {
                       Handlers.ParseTrainingRequest(dyn.ToObject<Gallop.SingleModeExecCommandRequest>());
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
            lock (_lock)
            {
                try
                {
                    var jsonstr = MessagePackSerializer.ConvertToJson(buffer);
                    //AnsiConsole.WriteLine(jsonstr);
                    var dyn = JsonConvert.DeserializeObject<dynamic>(jsonstr); 
                    if (dyn == default(dynamic)) return;
                    if (dyn == default(dynamic)) return;
                    var data = dyn.data;
                    if (data.chara_info?.scenario_id == 5 || (data != null && data.venus_data_set != null))
                    {
                        if (dyn.data.venus_data_set.race_start_info is JArray)
                            dyn.data.venus_data_set.race_start_info = null;
                        if (dyn.data.venus_data_set.venus_race_condition is JArray)
                            dyn.data.venus_data_set.venus_race_condition = null;
                    }
                    if (data.chara_info != null && data.home_info?.command_info_array != null && data.race_reward_info == null) //根据文本简单过滤防止重复、异常输出
                    {
                        if (Config.Get(Resource.ConfigSet_ShowCommandInfo))
                            Handlers.ParseCommandInfo(dyn.ToObject<Gallop.SingleModeCheckEventResponse>());
                    }
                    if (dyn.ToObject<Gallop.SingleModeCheckEventResponse>().data.command_result != null && dyn.ToObject<Gallop.SingleModeCheckEventResponse>().data.command_result.result_state == 1)//训练失败
                    {
                        AnsiConsole.MarkupLine($"[red]训练失败！[/]");
                        if (GameStats.stats[GameStats.currentTurn] != null)
                            GameStats.stats[GameStats.currentTurn].isTrainingFailed = true;
                    }
                    if (data.chara_info != null && data.unchecked_event_array?.Count > 0)
                    {
                        if (Config.Get(Resource.ConfigSet_ParseSingleModeCheckEventResponse))
                            Handlers.ParseSingleModeCheckEventResponse(dyn.ToObject<Gallop.SingleModeCheckEventResponse>());
                    }
                    if (data.chara_info != null && (data.chara_info.state == 2 || data.chara_info.state == 3) && data.unchecked_event_array?.Count == 0)
                    {
                        if (Config.Get(Resource.ConfigSet_MaximiumGradeSkillRecommendation) && data.chara_info.skill_tips_array != null)
                            Handlers.ParseSkillTipsResponse(dyn.ToObject<Gallop.SingleModeCheckEventResponse>());
                    }
                    if (data.trained_chara_array != null && data.trained_chara_favorite_array != null && data.room_match_entry_chara_id_array != null)
                    {
                        if (Config.Get(Resource.ConfigSet_ParseTrainedCharaLoadResponse))
                            Handlers.ParseTrainedCharaLoadResponse(dyn.ToObject<Gallop.TrainedCharaLoadResponse>());
                    }
                    if (data.user_info_summary != null && Config.Get(Resource.ConfigSet_ParseFriendSearchResponse))
                    {
                        if (data.practice_partner_info != null && data.support_card_data != null && data.follower_num != null && data.own_follow_num != null)
                            Handlers.ParseFriendSearchResponse(dyn.ToObject<Gallop.FriendSearchResponse>());
                        else if (data.user_info_summary.user_trained_chara != null)
                            Handlers.ParseFriendSearchResponseSimple(dyn.ToObject<Gallop.FriendSearchResponse>());
                    }
                    if (data.opponent_info_array?.Count == 3)
                    {
                        if (Config.Get(Resource.ConfigSet_ParseTeamStadiumOpponentListResponse))
                            Handlers.ParseTeamStadiumOpponentListResponse(dyn.ToObject<Gallop.TeamStadiumOpponentListResponse>()); //https://github.com/CNA-Bld/EXNOA-CarrotJuicer/issues/2
                    }
                    if (data.trained_chara_array != null && data.race_result_info != null && data.entry_info_array != null && data.practice_race_id != null && data.state != null && data.practice_partner_owner_info_array != null)
                    {
                        if (Config.Get(Resource.ConfigSet_ParsePracticeRaceRaceStartResponse))
                            Handlers.ParsePracticeRaceRaceStartResponse(dyn.ToObject<Gallop.PracticeRaceRaceStartResponse>());
                    }
                    if (data.race_scenario != null && data.random_seed != null && data.race_horse_data_array != null && data.trained_chara_array != null && data.season != null && data.weather != null && data.ground_condition != null)
                    {
                        if (Config.Get(Resource.ConfigSet_ParseRoomMatchRaceStartResponse))
                            Handlers.ParseRoomMatchRaceStartResponse(dyn.ToObject<Gallop.RoomMatchRaceStartResponse>());
                    }
                    if (data.room_info != null && data.room_user_array != null && data.race_horse_data_array != null && data.trained_chara_array != null)
                    {
                        if (Config.Get(Resource.ConfigSet_ParseChampionsRaceStartResponse))
                            Handlers.ParseChampionsRaceStartResponse(dyn.ToObject<Gallop.ChampionsFinalRaceStartResponse>());
                    }
                    if (dyn.data_headers.server_list != null && dyn.data_headers.server_list.resource_server_login != null)
                    {
                        AnsiConsole.MarkupLine($"[green]检测到ViewerID为{dyn.data_headers.viewer_id}的帐号登录请求[/]");
                    }
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                catch (Exception e)
                {
                    AnsiConsole.MarkupLine("[red]解析Response时出现错误: (如果程序运行正常则可以忽略)[/]");
                    AnsiConsole.WriteException(e);
                }
            }
        }
    }
}
