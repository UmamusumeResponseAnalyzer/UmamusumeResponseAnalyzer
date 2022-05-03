using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer
{
    /// <summary>
    /// 直接从Beta抓的包，然后重放
    /// </summary>
    internal static class DMM
    {
        internal static readonly string DMM_CONFIG_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", ".token");
        private const string AcceptEncoding = "gzip, deflate, br";
        private const string AcceptLanguage = "zh-CN";
        private const string UserAgent = "DMMGamePlayer5-Win/5.0.119 Electron/17.2.0";
        private const string ClientApp = "DMMGamePlayer5";
        private const string ClientVersion = "5.0.119";
        private const string SecFetchDest = "empty";
        private const string SecFetchMode = "no-cors";
        private const string SecFetchSite = "none";
        //User specific
        private static string login_session_id { get; set; } = string.Empty;
        private static string login_secure_id { get; set; } = string.Empty;
        private static string mac_address { get; set; } = string.Empty;
        private static string hdd_serial { get; set; } = string.Empty;
        private static string motherboard { get; set; } = string.Empty;
        private static string user_os { get; set; } = string.Empty;
        private static string umamusume_file_path { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Umamusume", "umamusume.exe");

        static DMM()
        {
            if (!File.Exists(DMM_CONFIG_FILEPATH)) return;
            var lines = File.ReadAllLines(DMM_CONFIG_FILEPATH).Where(x => !string.IsNullOrEmpty(x));
            foreach (var i in lines)
            {
                var split = i.Split('=');
                switch (split[0])
                {
                    case nameof(login_session_id):
                        login_session_id = split[1];
                        break;
                    case nameof(login_secure_id):
                        login_secure_id = split[1];
                        break;
                    case nameof(mac_address):
                        mac_address = split[1];
                        break;
                    case nameof(hdd_serial):
                        hdd_serial = split[1];
                        break;
                    case nameof(motherboard):
                        motherboard = split[1];
                        break;
                    case nameof(user_os):
                        user_os = split[1];
                        break;
                    case nameof(umamusume_file_path):
                        umamusume_file_path = split[1];
                        break;
                    default:
                        throw new Exception($"Unknown .token key {split[0]}");
                }
            }
        }
        public static async ValueTask<string?> GetExecuteArgsAsync()
        {
            var cookies = new System.Net.CookieContainer();
            var client = new HttpClient(new HttpClientHandler
            {
                CookieContainer = cookies
            });
            client.DefaultRequestHeaders.Add("Accept-Encoding", AcceptEncoding);
            client.DefaultRequestHeaders.Add("Accept-Language", AcceptLanguage);
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            client.DefaultRequestHeaders.Add("Client-App", ClientApp);
            client.DefaultRequestHeaders.Add("Client-version", ClientVersion);
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", SecFetchDest);
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", SecFetchMode);
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", SecFetchSite);
            cookies.Add(new System.Net.Cookie(nameof(login_session_id), login_session_id) { Domain = "apidgp-gameplayer.games.dmm.com" });
            cookies.Add(new System.Net.Cookie(nameof(login_secure_id), login_secure_id) { Domain = "apidgp-gameplayer.games.dmm.com" });

            await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/gameinfo", new StringContent($"{{\"product_id\":\"umamusume\",\"game_type\":\"GCL\",\"game_os\":\"win\",\"mac_address\":\"{mac_address}\",\"hdd_serial\":\"{hdd_serial}\",\"motherboard\":\"{motherboard}\",\"user_os\":\"{user_os}\"}}", Encoding.UTF8, "application/json"));
            var response = await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/launch/cl", new StringContent($"{{\"product_id\":\"umamusume\",\"game_type\":\"GCL\",\"game_os\":\"win\",\"launch_type\":\"LIB\",\"mac_address\":\"{mac_address}\",\"hdd_serial\":\"{hdd_serial}\",\"motherboard\":\"{motherboard}\",\"user_os\":\"{user_os}\"}}", Encoding.UTF8, "application/json"));
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/report", new StringContent("{\"type\":\"start\",\"product_id\":\"umamusume\",\"game_type\":\"GCL\"}", Encoding.UTF8, "application/json"));

            return json["data"]?["execute_args"]?.ToString();
        }
        public static void Launch(string args)
        {
            using var Proc = new Process();
            var StartInfo = new ProcessStartInfo
            {
                FileName = umamusume_file_path,
                Arguments = args,
                CreateNoWindow = false,
                UseShellExecute = true,
                Verb = "runas"
            };
            Proc.StartInfo = StartInfo;
            Proc.Start();
        }
    }
}
