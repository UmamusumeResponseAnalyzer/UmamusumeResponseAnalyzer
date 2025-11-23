using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UmamusumeResponseAnalyzer.Plugin;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using static UmamusumeResponseAnalyzer.Localization.Server;

namespace UmamusumeResponseAnalyzer
{
    internal static class Server
    {
        private static WebserverLite server = new(new WebserverSettings(Config.Core.ListenAddress, Config.Core.ListenPort), (ctx) => { return ctx.Response.Send(string.Empty); });
        public static readonly ManualResetEvent OnPing = new(false);
        public static bool IsRunning => server.IsListening;
        public static void Start()
        {
            server.Routes.PreAuthentication.Static.Add(WatsonWebserver.Core.HttpMethod.POST, "/notify/response", (ctx) =>
            {
                var buffer = ctx.Request.DataAsBytes;
#if DEBUG
                Directory.CreateDirectory("packets");
                File.WriteAllText($@"./packets/{DateTime.Now:yy-MM-dd HH-mm-ss-fff}{Random.Shared.Next(000, 999)}R.json", JObject.Parse(MessagePackSerializer.ConvertToJson(buffer)).ToString());
#endif
                if (Config.Misc.SaveResponseForDebug)
                {
                    if (Directory.Exists("packets"))
                    {
                        foreach (var i in Directory.GetFiles("packets"))
                        {
                            var fileInfo = new FileInfo(i);
                            if (fileInfo.CreationTime.AddDays(1) < DateTime.Now)
                                fileInfo.Delete();
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory("packets");
                    }
                    File.WriteAllBytes($"packets/{DateTime.Now:yy-MM-dd HH-mm-ss-fff}R.msgpack", buffer);
                }
                _ = Task.Run(() => ParseResponse(buffer));
                return ctx.Response.Send(string.Empty);
            });
            server.Routes.PreAuthentication.Static.Add(WatsonWebserver.Core.HttpMethod.POST, "/notify/request", (ctx) =>
            {
                var buffer = ctx.Request.DataAsBytes;
                if (Config.Core.RequestAdditionalHeader)
                    _ = Task.Run(() => ParseRequest(buffer[170..]));
                else
                    _ = Task.Run(() => ParseRequest(buffer));
                return ctx.Response.Send(string.Empty);
            });
            server.Routes.PreAuthentication.Static.Add(WatsonWebserver.Core.HttpMethod.GET, "/notify/ping", (ctx) =>
            {
                AnsiConsole.MarkupLine(I18N_PingReceived);
                OnPing.Signal();
                return ctx.Response.Send("pong");
            });
            server.Start();
            foreach (var plugin in PluginManager.LoadedPlugins)
            {
                AnsiConsole.MarkupLine($"插件{plugin.Name}[lightgreen]加载成功[/]");
            }
            foreach (var plugin in PluginManager.FailedPlugins)
            {
                AnsiConsole.MarkupLine($"插件{Path.GetFileName(plugin)}[red]加载失败[/] ({plugin})");
            }
            if (Config.Core.ListenAddress == "0.0.0.0")
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                       .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                       .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                       .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                       .Select(x => x.Address.ToString())
                       .ToList();
                foreach (var i in interfaces)
                {
                    AnsiConsole.WriteLine(I18N_AvailableEndpointTip, i, Config.Core.ListenPort);
                }
            }
        }
        public static void Stop() => server.Dispose();
        static void ParseRequest(byte[] buffer)
        {
            try
            {
                var str = MessagePackSerializer.ConvertToJson(buffer);
                var obj = JsonConvert.DeserializeObject<JObject>(str);
#if DEBUG
                Directory.CreateDirectory("packets");
                if (Config.Core.RequestAdditionalHeader)
                    File.WriteAllText($@"./packets/{DateTime.Now:yy-MM-dd HH-mm-ss-fff}{Random.Shared.Next(000, 999)}Q.json", obj?.ToString() ?? string.Empty);
                else
                    File.WriteAllText($@"./packets/{DateTime.Now:yy-MM-dd HH-mm-ss-fff}{Random.Shared.Next(000, 999)}Q.json", obj?.ToString() ?? string.Empty);
#endif
                if (obj == default) return;

                foreach (var (k, v) in PluginManager.RequsetAnalyzerMethods)
                {
                    foreach (var (self, method) in v)
                    {
                        try
                        {
                            method.Invoke(self, [obj]);
                        }
                        catch (Exception e)
                        {
                            AnsiConsole.WriteException(e);
                        }
                    }
                }
            }
            catch
            {
                if (!Config.Core.RequestAdditionalHeader) buffer = buffer[170..];
                Config.Core.RequestAdditionalHeader = !Config.Core.RequestAdditionalHeader;
                Config.Save();
            }
        }
        static void ParseResponse(byte[] buffer)
        {
            try
            {
                var jsonstr = MessagePackSerializer.ConvertToJson(buffer);
                var obj = JsonConvert.DeserializeObject<JObject>(jsonstr);
                if (obj == default) return;
                if (obj["data"] is JObject data)
                {
                    // 如果在选技能时退出游戏重新进入，会套一层“single_mode_load_common”，在这里去掉这层
                    if (data["single_mode_load_common"] is JObject common)
                    {
                        var key = data.Properties().FirstOrDefault(x => x.Name.EndsWith("_data_set"))?.Name;
                        if (key != default)
                        {
                            common[key] = data[key];
                        }
                        obj.Remove("data");
                        obj.Add("data", common);
                        data = common; // 这一行是给下面用的，不然data还是最初的那个
                    }

                    if (data["chara_info"] is JObject chara_info)
                    {
                        var scenario_id = chara_info["scenario_id"].ToInt();
                        if (scenario_id == 5 && data.TryGetValue("venus_data_set", out var ds))
                        {
                            if (ds["race_start_info"] is JArray)
                                ds["race_start_info"] = null;
                            if (ds["venus_race_condition"] is JArray)
                                ds["venus_race_condition"] = null;
                            obj["data"]!["venus_data_set"] = ds;
                        }
                        if (scenario_id == 8 && data.TryGetValue("cook_data_set", out ds))
                        {
                            if (ds["dish_skill_info"] is JArray)
                                ds["dish_skill_info"] = null;
                            if (ds["gain_material_info"] is JArray)
                                ds["gain_material_info"] = null;
                            if (ds["last_command_info"] is JArray)
                                ds["last_command_info"] = null;
                            obj["data"]!["cook_data_set"] = ds;
                        }
                        if (scenario_id == 10 && data.TryGetValue("legend_data_set", out ds))
                        {
                            if (ds["cm_info"] is JObject cm_info && cm_info["race_result_info"] is JArray)
                                cm_info["race_result_info"] = null;
                            if (ds["popularity_info"] is JArray)
                                ds["popularity_info"] = null;
                            else if (ds["popularity_info"] is JObject popularity_info && popularity_info["poster_race_result_info"] is JArray)
                                popularity_info["poster_race_result_info"] = null;
                            obj["data"]!["legend_data_set"] = ds;
                        }
                        if (scenario_id == 11 && data.TryGetValue("pioneer_data_set", out ds))
                        {
                            if (ds["shima_training_info"] is JArray)
                                ds["shima_training_info"] = null;
                            obj["data"]!["pioneer_data_set"] = ds;
                        }
                    }
                }

                foreach (var (k, v) in PluginManager.ResponseAnalyzerMethods)
                {
                    foreach (var (self, method) in v)
                    {
                        try
                        {
                            method.Invoke(self, [obj]);
                        }
                        catch (Exception e)
                        {
                            AnsiConsole.WriteException(e);
                        }
                    }
                }
            }
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
}
