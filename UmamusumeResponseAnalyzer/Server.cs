using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using UmamusumeResponseAnalyzer.Communications;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Handler;
using static UmamusumeResponseAnalyzer.Localization.Server;

namespace UmamusumeResponseAnalyzer
{
    internal static class Server
    {
        private static HttpListener httpListener;
        private static readonly object _lock = new();
        private static Mutex Mutex;
        private static readonly Dictionary<string, WebSocket> connectedWebsockets = [];
        private static readonly JsonSerializerSettings jsonSerializerSettings = new() { NullValueHandling = NullValueHandling.Ignore };
        public static readonly ManualResetEvent OnPing = new(false);
        public static bool IsRunning => httpListener.IsListening;
        public static void Start()
        {
            try
            {
                httpListener = new();
                httpListener.Prefixes.Add("http://*:4693/");
                httpListener.Start();
                AnsiConsole.MarkupLine(I18N_WildcardServerStarted);
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                   .Where(x => x.NetworkInterfaceType == NetworkInterfaceType.Ethernet && x.OperationalStatus == OperationalStatus.Up)
                   .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                   .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                   .Select(x => x.Address.ToString())
                   .ToList();
                foreach (var i in interfaces)
                {
                    AnsiConsole.WriteLine(I18N_AvailableEndpointTip, i);
                }
            }
            catch
            {
                try
                {
                    httpListener = new();
                    httpListener.Prefixes.Add("http://127.0.0.1:4693/");
                    httpListener.Start();
                    AnsiConsole.MarkupLine(I18N_NormalServerStarted);
                }
                catch (HttpListenerException)
                {
                    AnsiConsole.WriteLine(I18N_ServerStartFail);
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

                        // 处理websocket请求
                        if (ctx.Request.IsWebSocketRequest)
                        {
                            _ = HandleWebsocket(ctx);
                        }
                        // 处理http请求
                        else
                        {
                            _ = HandleHttp(ctx);
                        }
                    }
                    catch
                    {
                    }
                }
            });
        }
        public static async Task<bool> Send(string wsKey, object response)
        {
            if (connectedWebsockets.TryGetValue(wsKey, out var ws))
            {
                var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response, jsonSerializerSettings));
                await ws.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
                return true;
            }
            else
            {
                return false;
            }
        }
        static void ParseRequest(byte[] buffer)
        {
            try
            {
                lock (_lock)
                {
                    var str = MessagePackSerializer.ConvertToJson(buffer);
                    var dyn = JsonConvert.DeserializeObject<dynamic>(str);
                    if (dyn == default(dynamic)) return;

                    if (!dyn.GetType().IsValueType && dyn.command_type != null && dyn.command_type == 1) //玩家点击了训练
                    {
                        Handlers.ParseTrainingRequest(dyn.ToObject<Gallop.SingleModeExecCommandRequest>());
                    }
                    if (dyn.choice_number != null && dyn.choice_number > 0)  // 玩家点击了事件
                    {
                        Handlers.ParseChoiceRequest(dyn.ToObject<Gallop.SingleModeChoiceRequest>());
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
                    var dyn = JsonConvert.DeserializeObject<dynamic>(jsonstr);
                    if (dyn == null) return;
                    if (dyn.data == null) return;
                    var data = dyn.data;
                    #region pre-fix
                    // 如果在选技能时退出游戏重新进入，会套一层“single_mode_load_common”，在这里去掉这层
                    if (data.single_mode_load_common != null)
                    {
                        var data1 = data.single_mode_load_common;
                        if (data.arc_data_set != null)
                        {
                            data1.arc_data_set = data.arc_data_set;
                        }
                        if (data.venus_data_set != null)
                        {
                            data1.venus_data_set = data.venus_data_set;
                        }
                        if (data.sport_data_set != null)
                        {
                            data1.sport_data_set = data.sport_data_set;
                        }
                        data = data1;
                        dyn.data = data;
                    }
                    // 修复崩溃问题
                    if (data.chara_info?.scenario_id == 5 || data.venus_data_set != null)
                    {
                        if (dyn.data.venus_data_set.race_start_info is JArray)
                            dyn.data.venus_data_set.race_start_info = null;
                        if (dyn.data.venus_data_set.venus_race_condition is JArray)
                            dyn.data.venus_data_set.venus_race_condition = null;
                    }
                    #endregion
                    if (data.chara_info != null && data.home_info?.command_info_array != null && data.race_reward_info == null && !(data.chara_info.state == 2 || data.chara_info.state == 3)) //根据文本简单过滤防止重复、异常输出
                    {
                        if (Config.Get(Localization.Config.I18N_ShowCommandInfo))
                        {
                            if (data.chara_info.scenario_id == 7)
                            {
                                //File.WriteAllText("package.json", jsonstr);
                                //Handlers.GameLogger(dyn.ToObject<Gallop.SingleModeCheckEventResponse>());
                                Handlers.ParseSportCommandInfo(dyn.ToObject<Gallop.SingleModeCheckEventResponse>());
                            }
                            else
                                Handlers.ParseCommandInfo(dyn.ToObject<Gallop.SingleModeCheckEventResponse>());
                        }
                    }
                    if (dyn.data.command_result != null) // 训练结果
                    {
                        if (dyn.data.command_result.result_state == 1) // 训练失败
                        {
                            AnsiConsole.MarkupLine(I18N_TrainingFailed);
                            if (GameStats.stats[GameStats.currentTurn] != null)
                                GameStats.stats[GameStats.currentTurn].isTrainingFailed = true;
                        }
                        EventLogger.Start(dyn.ToObject<Gallop.SingleModeCheckEventResponse>()); // 开始记录事件，跳过从上一次调用update到这里的所有事件和训练
                    }
                    if (data.chara_info != null && data.unchecked_event_array?.Count > 0)
                    {
                        if (Config.Get(Localization.Config.I18N_ParseSingleModeCheckEventResponse))
                            Handlers.ParseSingleModeCheckEventResponse(dyn.ToObject<Gallop.SingleModeCheckEventResponse>());
                    }
                    if (data.chara_info != null && (data.chara_info.state == 2 || data.chara_info.state == 3) && data.unchecked_event_array?.Count == 0)
                    {
                        if (Config.Get(Localization.Config.I18N_MaximiumGradeSkillRecommendation) && data.chara_info.skill_tips_array != null)
                            Handlers.ParseSkillTipsResponse(dyn.ToObject<Gallop.SingleModeCheckEventResponse>());
                    }
                    if (data.trained_chara_array != null && data.trained_chara_favorite_array != null && data.room_match_entry_chara_id_array != null)
                    {
                        if (Config.Get(Localization.Config.I18N_ParseTrainedCharaLoadResponse))
                            Handlers.ParseTrainedCharaLoadResponse(dyn.ToObject<Gallop.TrainedCharaLoadResponse>());
                    }
                    if (data.user_info_summary != null && Config.Get(Localization.Config.I18N_ParseFriendSearchResponse))
                    {
                        if (data.practice_partner_info != null && data.support_card_data != null && data.follower_num != null && data.own_follow_num != null)
                            Handlers.ParseFriendSearchResponse(dyn.ToObject<Gallop.FriendSearchResponse>());
                        else if (data.user_info_summary.user_trained_chara != null)
                            Handlers.ParseFriendSearchResponseSimple(dyn.ToObject<Gallop.FriendSearchResponse>());
                    }
                    if (data.opponent_info_array?.Count == 3)
                    {
                        if (Config.Get(Localization.Config.I18N_ParseTeamStadiumOpponentListResponse))
                            Handlers.ParseTeamStadiumOpponentListResponse(dyn.ToObject<Gallop.TeamStadiumOpponentListResponse>()); //https://github.com/CNA-Bld/EXNOA-CarrotJuicer/issues/2
                    }
                    if (data.trained_chara_array != null && data.race_result_info != null && data.entry_info_array != null && data.practice_race_id != null && data.state != null && data.practice_partner_owner_info_array != null)
                    {
                        if (Config.Get(Localization.Config.I18N_ParsePracticeRaceRaceStartResponse))
                            Handlers.ParsePracticeRaceRaceStartResponse(dyn.ToObject<Gallop.PracticeRaceRaceStartResponse>());
                    }
                    if (data.race_scenario != null && data.random_seed != null && data.race_horse_data_array != null && data.trained_chara_array != null && data.season != null && data.weather != null && data.ground_condition != null)
                    {
                        if (Config.Get(Localization.Config.I18N_ParseRoomMatchRaceStartResponse))
                            Handlers.ParseRoomMatchRaceStartResponse(dyn.ToObject<Gallop.RoomMatchRaceStartResponse>());
                    }
                    if (data.room_info != null && data.room_user_array != null && data.race_horse_data_array != null && data.trained_chara_array != null)
                    {
                        if (Config.Get(Localization.Config.I18N_ParseChampionsRaceStartResponse))
                            Handlers.ParseChampionsRaceStartResponse(dyn.ToObject<Gallop.ChampionsFinalRaceStartResponse>());
                    }
                    if (dyn.data_headers.server_list != null && dyn.data_headers.server_list.resource_server_login != null)
                    {
                        AnsiConsole.MarkupLine(I18N_LoginRequestDetected, dyn.data_headers.viewer_id);
                    }
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                catch (Exception e)
                {
                    AnsiConsole.MarkupLine(I18N_ResponseAnalyzeFail);
                    AnsiConsole.WriteException(e);
#if DEBUG
                    throw;
#endif
                }
            }
        }
        static async Task HandleWebsocket(HttpListenerContext ctx)
        {
            var wsctx = await ctx.AcceptWebSocketAsync(null);
            var ws = wsctx.WebSocket;
            try
            {
                connectedWebsockets.Add(wsctx.SecWebSocketKey, ws);
                var wsbuffer = new byte[64];
                var contentbuffer = new List<byte>();
                while (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        var msg = await ws.ReceiveAsync(wsbuffer, CancellationToken.None);
                        contentbuffer.AddRange(msg.EndOfMessage ? wsbuffer[..msg.Count] : wsbuffer);

                        if (msg.EndOfMessage)
                        {
                            if (msg.MessageType == WebSocketMessageType.Close)
                            {
                                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                            }
                            else if (msg.MessageType == WebSocketMessageType.Text)
                            {
                                var req = JsonConvert.DeserializeObject<WSRequest>(Encoding.UTF8.GetString(contentbuffer.ToArray())) ?? throw new Exception(I18N_WebSocket_DeserializeRequestFail);
                                if (req.CommandType == CommandType.None) throw new Exception(string.Format(I18N_WebSocket_CommandTypeNone, wsctx.SecWebSocketKey));
                                var commandName = req.CommandType switch
                                {
                                    CommandType.Action => $"UmamusumeResponseAnalyzer.Communications.Actions.{req.Command}",
                                    CommandType.Subscribe or CommandType.Unsubscribe => $"UmamusumeResponseAnalyzer.Communications.Subscriptions.{req.Command}"
                                };
                                var commandParameters = req.CommandType switch
                                {
                                    CommandType.Action => req.Parameters,
                                    CommandType.Subscribe or CommandType.Unsubscribe => req.Parameters.Prepend(wsctx.SecWebSocketKey).ToArray()
                                };
                                var commandType = Type.GetType(commandName) ?? throw new Exception(string.Format(I18N_WebSocket_CommandNotFound, commandName));
                                var commandConstructor = commandType.GetConstructor(Enumerable.Repeat(typeof(string), commandParameters.Length).ToArray()) ?? throw new Exception(I18N_WebSocket_ConstructorNotFound);
                                var command = (ICommand)commandConstructor.Invoke(commandParameters);
                                var response = req.CommandType switch
                                {
                                    CommandType.Unsubscribe => commandType?.GetMethod("Cancel")?.Invoke(command, null),
                                    _ => command.Execute()
                                };
                                if (response != null)
                                {
                                    await ws.SendAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)), WebSocketMessageType.Text, msg.EndOfMessage, CancellationToken.None);
                                }
                                contentbuffer.Clear();
                            }
                        }
                    }
                    catch (WebSocketException) { }
                    catch (Exception ex)
                    {
                        AnsiConsole.WriteException(ex);
                        contentbuffer.Clear();
                    }
                }
            }
            catch { }
            finally
            {
                ws.Dispose();
                connectedWebsockets.Remove(wsctx.SecWebSocketKey);
            }
        }
        static async Task HandleHttp(HttpListenerContext ctx)
        {
            using var ms = new MemoryStream();
            ctx.Request.InputStream.CopyTo(ms);
            var buffer = ms.ToArray();

            if (ctx.Request.RawUrl == "/notify/response")
            {
#if DEBUG
                Directory.CreateDirectory("packets");
                File.WriteAllBytes($@"./packets/{DateTime.Now:yy-MM-dd HH-mm-ss-fff}R.bin", buffer);
                File.WriteAllText($@"./packets/Turn{GameStats.currentTurn}_{DateTime.Now:yy-MM-dd HH-mm-ss-fff}R.json", JObject.Parse(MessagePackSerializer.ConvertToJson(buffer)).ToString());
#endif
                if (Config.Get(Localization.Config.I18N_SaveResponseForDebug))
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
#if DEBUG
                Directory.CreateDirectory("packets");
                File.WriteAllText($@"./packets/Turn{GameStats.currentTurn}_{DateTime.Now:yy-MM-dd HH-mm-ss-fff}Q.json", JObject.Parse(MessagePackSerializer.ConvertToJson(buffer.AsMemory()[170..])).ToString());
#endif
                _ = Task.Run(() => ParseRequest(buffer[170..]));
            }
            else if (ctx.Request.RawUrl == "/notify/ping")
            {
                AnsiConsole.MarkupLine(I18N_PingReceived);
                await ctx.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("pong"));
                ctx.Response.Close();
                OnPing.Signal();
                return;
            }

            await ctx.Response.OutputStream.WriteAsync(Array.Empty<byte>());
            ctx.Response.Close();
        }
    }
}
