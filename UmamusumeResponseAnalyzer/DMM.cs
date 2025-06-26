using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using static UmamusumeResponseAnalyzer.DMMConfig;
using static UmamusumeResponseAnalyzer.Localization.DMM;
using static UmamusumeResponseAnalyzer.Localization.LaunchMenu;

namespace UmamusumeResponseAnalyzer
{
    /// <summary>
    /// 直接抓的包，然后重放
    /// </summary>
    internal static class DMM
    {
        public static bool IgnoreExistProcess = false;

        public static async Task<string> LoginUrl()
        {
            using var client = GetHttpClient();
            var resp = await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/auth/login/url", new StringContent("{\"prompt\": \"choose\"}", Encoding.UTF8, "application/json"));
            var restr = await resp.Content.ReadAsStringAsync();
            var jo = JObject.Parse(restr);
            return jo["data"]!["url"]!.ToString();
        }
        public static async Task<string> IssueAccessToken(string code)
        {
            using var client = GetHttpClient();
            var resp = await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/auth/accesstoken/issue", new StringContent($"{{\"code\": \"{code}\"}}", Encoding.UTF8, "application/json"));
            var restr = await resp.Content.ReadAsStringAsync();
            var jo = JObject.Parse(restr);
            var token = jo["data"]!["access_token"]!.ToString();
            return token;
        }
        public static async Task<string?> GetExecuteArgsAsync(DMMAccountInformation account)
        {
            using var client = GetHttpClient();
            client.DefaultRequestHeaders.Add("actauth", account.access_token);
            using var response = await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/r2/launch/cl", new StringContent($"{{\"product_id\":\"umamusume\",\"game_type\":\"GCL\",\"game_os\":\"win\",\"launch_type\":\"LIB\",\"mac_address\":\"{Config.DMM.MachineInformation.mac_address}\",\"hdd_serial\":\"{Config.DMM.MachineInformation.hdd_serial}\",\"motherboard\":\"{Config.DMM.MachineInformation.motherboard}\",\"user_os\":\"{Config.DMM.MachineInformation.user_os}\"}}", Encoding.UTF8, "application/json"));
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            if (json["result_code"]?.ToObject<int>() == 203)
            {
                return "DMM session has expired";
            }
            //var version = new Version(json["data"]!["latest_version"]!.ToString());
            //if (GetGameVersion() < version)
            //{
            //    var file_list_url = json["data"]!["file_list_url"]!.ToString();
            //    using var resp = await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/getCookie", new StringContent($"{{\"url\":\"https://cdn-gameplayer.games.dmm.com/product/umamusume/Umamusume/content/win/{version}/data/*\"}}"));
            //    var cookie = JObject.Parse(await resp.Content.ReadAsStringAsync());
            //    await UpdateGame(file_list_url, cookie);
            //}
            //await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/report", new StringContent("{\"type\":\"start\",\"product_id\":\"umamusume\",\"game_type\":\"GCL\"}", Encoding.UTF8, "application/json"));

            return json["data"]?["execute_args"]?.ToString();
        }
        public static async Task RunUmamusume(DMMAccountInformation account)
        {
            await AnsiConsole.Status().StartAsync(I18N_Start_Checking, async ctx =>
            {
                var processes = Process.GetProcessesByName("umamusume");
                AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_Start_Checking_Found, processes.Length)));
                if (processes.Length == 0 || IgnoreExistProcess)
                {
                    ctx.Spinner(Spinner.Known.BouncingBar);
                    ctx.Status(I18N_Start_GetToken);

                    using var proc = new Process(); //检查下载的文件是否正常
                    var dmmToken = await GetExecuteArgsAsync(account);

                    switch (dmmToken)
                    {
                        case "DMM session has expired":
                            {
                                AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_DMMTokenExpired));
                                return;
                            }
                        case "":
                            {

                                AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Start_TokenFailed));
                                return;
                            }
                        default:

                            AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, I18N_Start_TokenGot));
                            break;
                    }
                    ctx.Status(I18N_Start_Launching);
                    if (!string.IsNullOrEmpty(dmmToken))
                    {
                        var path = !string.IsNullOrEmpty(account.split_umamusume_file_path)
                            ? account.split_umamusume_file_path
                            : Config.DMM.MachineInformation.umamusume_file_path;
                        var configFilepath = Path.Combine(Path.GetDirectoryName(path)!, "config.json");
                        if (!File.Exists(configFilepath))
                        {
                            Launch(account, dmmToken);
                        }
                        else
                        {
                            var config = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(File.ReadAllText(configFilepath))!;
                            if (string.IsNullOrEmpty(account.savedata_file_path))
                            {
                                config.Remove("savedata_path");
                                File.WriteAllText(configFilepath, config.ToString(Newtonsoft.Json.Formatting.Indented));
                                Launch(account, dmmToken);
                            }
                            else
                            {
                                if (config.ContainsKey("savedata_path"))
                                {
                                    var prev = config["savedata_path"]!.ToString();
                                    config["savedata_path"] = account.savedata_file_path;
                                    File.WriteAllText(configFilepath, config.ToString(Newtonsoft.Json.Formatting.Indented));
                                    config["savedata_path"] = prev;
                                }
                                else
                                {
                                    config["savedata_path"] = account.savedata_file_path;
                                    File.WriteAllText(configFilepath, config.ToString(Newtonsoft.Json.Formatting.Indented));
                                    config.Remove("savedata_path");
                                }
                                Launch(account, dmmToken);
                                Server.OnPing.Wait(() => File.WriteAllText(configFilepath, config.ToString(Newtonsoft.Json.Formatting.Indented)));
                            }
                        }
                    }
                }
                else
                {
                    ctx.Status(I18N_Start_Checking_AlreadyRunning);
                    foreach (var process in processes) process.Dispose();
                }
            });
        }
        static void Launch(DMMAccountInformation account, string args)
        {
            try
            {
                var path = !string.IsNullOrEmpty(account.split_umamusume_file_path)
                    ? account.split_umamusume_file_path
                    : Config.DMM.MachineInformation.umamusume_file_path;
                using var Proc = new Process();
                var StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = args,
                    CreateNoWindow = false,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Proc.StartInfo = StartInfo;
                Proc.Start();
            }
            catch (Win32Exception)
            {
                AnsiConsole.WriteLine(I18N_AppLaunchCanceled);
            }
        }
        /*Version GetGameVersion()
        {
            using var fs = new FileStream(Path.Combine(Path.GetDirectoryName(umamusume_file_path)!, "umamusume_Data", "globalgamemanagers"), FileMode.Open);
            fs.Seek(0x1214, SeekOrigin.Begin);
            var version = new byte[6];
            fs.Read(version, 0, 6);
            fs.Close();
            return new Version(Encoding.UTF8.GetString(version));
        }
        async Task UpdateGame(string file_list_url, JObject cookie)
        {
            var applicationRootPath = Path.GetDirectoryName(umamusume_file_path);
            if (applicationRootPath == default) throw new Exception(I18N_AppRootPathNull);
            var cookies = new CookieContainer();
            using var client = new HttpClient(new HttpClientHandler
            {
                CookieContainer = cookies
            });
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
            client.DefaultRequestHeaders.Add("User-Agent", "Go-http-client/2.0");
            var totalPages = JObject.Parse(await (await client.GetAsync($"https://apidgp-gameplayer.games.dmm.com{file_list_url}/totalpages")).Content.ReadAsStringAsync())["data"]!["total_pages"]!.Value<int>();
            var downloads = new List<JToken>();
            foreach (var i in Enumerable.Range(1, totalPages))
            {
                var page = JObject.Parse(await (await client.GetAsync($"https://apidgp-gameplayer.games.dmm.com{file_list_url}?page={i}")).Content.ReadAsStringAsync());
                foreach (var j in page["data"]!["file_list"]!)
                {
                    downloads.Add(j);
                }
            }
            client.DefaultRequestHeaders.Remove("Accept-Encoding");
            cookies.Add(new Cookie("CloudFront-Key-Pair-Id", cookie["key"]!.ToString()) { Domain = "cdn-gameplayer.games.dmm.com" });
            cookies.Add(new Cookie("CloudFront-Signature", cookie["signature"]!.ToString()) { Domain = "cdn-gameplayer.games.dmm.com" });
            cookies.Add(new Cookie("CloudFront-Policy", cookie["policy"]!.ToString()) { Domain = "cdn-gameplayer.games.dmm.com" });
            Parallel.ForEach(downloads, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (download) =>
            {
                var local_path = download["local_path"]!.ToString();
                var remote_hash = download["hash"]!.ToString();
                var remote_path = $"https://cdn-gameplayer.games.dmm.com/" + download["path"]!.ToString();
                local_path = Path.Combine(applicationRootPath, local_path);
                using var fs = new FileStream(local_path, FileMode.Create);
                do
                {
                    var local_hash = BitConverter.ToString(MD5.Create().ComputeHash(new FileStream(local_path, FileMode.Open))).Replace("-", string.Empty).ToLower();
                    if (local_hash == remote_hash) return;
                    using var stream = await client.GetStreamAsync(remote_path);
                    stream.CopyTo(fs);
                } while (!BitConverter.ToString(MD5.Create().ComputeHash(fs)).Replace("-", string.Empty).Equals(remote_hash, StringComparison.CurrentCultureIgnoreCase));
            });
        }
        */
        private static HttpClient GetHttpClient()
        {
            var cookies = new CookieContainer();
            var client = new HttpClient(new HttpClientHandler
            {
                CookieContainer = cookies
            });
            client.DefaultRequestHeaders.Add("Accept-Encoding", Config.DMM.LauncherInfomation.AcceptEncoding);
            client.DefaultRequestHeaders.Add("Accept-Language", Config.DMM.LauncherInfomation.AcceptLanguage);
            client.DefaultRequestHeaders.Add("User-Agent", Config.DMM.LauncherInfomation.UserAgent);
            client.DefaultRequestHeaders.Add("Client-App", Config.DMM.LauncherInfomation.ClientApp);
            client.DefaultRequestHeaders.Add("Client-version", Config.DMM.LauncherInfomation.ClientVersion);
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", Config.DMM.LauncherInfomation.SecFetchDest);
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", Config.DMM.LauncherInfomation.SecFetchMode);
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", Config.DMM.LauncherInfomation.SecFetchSite);

            return client;
        }
    }
}
