using IniParser;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using static UmamusumeResponseAnalyzer.Localization.DMM;
using static UmamusumeResponseAnalyzer.Localization.LaunchMenu;

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
        private const string UserAgent = "DMMGamePlayer5-Win/5.2.31 Electron/27.0.2";
        private const string ClientApp = "DMMGamePlayer5";
        private const string ClientVersion = "5.2.31";
        private const string SecFetchDest = "empty";
        private const string SecFetchMode = "no-cors";
        private const string SecFetchSite = "none";
        static internal string mac_address { get; set; } = string.Empty;
        static internal string hdd_serial { get; set; } = string.Empty;
        static internal string motherboard { get; set; } = string.Empty;
        static internal string user_os { get; set; } = string.Empty;
        static internal string umamusume_file_path { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Umamusume", "umamusume.exe");
        public static bool IgnoreExistProcess = false;
        public static List<DMMAccount> Accounts { get; set; } = new();

        static DMM()
        {
            if (!File.Exists(DMM_CONFIG_FILEPATH)) File.Create(DMM_CONFIG_FILEPATH).Dispose();
            var config = new FileIniDataParser().ReadFile(DMM_CONFIG_FILEPATH, Encoding.UTF8);
            if (config.Sections.Any(x => x.SectionName == "information"))
            {
                foreach (var i in config["information"])
                {
                    switch (i.KeyName)
                    {
                        case nameof(mac_address):
                            mac_address = i.Value;
                            break;
                        case nameof(hdd_serial):
                            hdd_serial = i.Value;
                            break;
                        case nameof(motherboard):
                            motherboard = i.Value;
                            break;
                        case nameof(user_os):
                            user_os = i.Value;
                            break;
                        case nameof(umamusume_file_path):
                            umamusume_file_path = i.Value;
                            break;
                        default:
                            throw new Exception(string.Format(I18N_UnknownTokenKey, i.KeyName));
                    }
                }
            }
            foreach (var i in config.Sections.Where(x => x.SectionName != "information"))
            {
                var dmm = new DMMAccount();
                dmm.Name = i.SectionName;
                dmm.login_secure_id = i.Keys[nameof(dmm.login_secure_id)];
                dmm.login_session_id = i.Keys[nameof(dmm.login_session_id)];
                dmm.savedata_file_path = i.Keys[nameof(dmm.savedata_file_path)];
                dmm.split_umamusume_file_path = i.Keys[nameof(dmm.split_umamusume_file_path)];
                if (!string.IsNullOrEmpty(dmm.login_secure_id) && !string.IsNullOrEmpty(dmm.login_session_id))
                    Accounts.Add(dmm);
            }
        }
        public static void Save()
        {
            var parser = new FileIniDataParser();
            var config = parser.ReadFile(DMM_CONFIG_FILEPATH, Encoding.UTF8);
            if (config.Sections.Any(x => x.SectionName == "information"))
            {
                config["information"][nameof(mac_address)] = mac_address;
                config["information"][nameof(hdd_serial)] = hdd_serial;
                config["information"][nameof(motherboard)] = motherboard;
                config["information"][nameof(user_os)] = user_os;
                config["information"][nameof(umamusume_file_path)] = umamusume_file_path;
            }
            else
            {
                config.Sections.AddSection("information");
                config["information"].AddKey(nameof(mac_address), mac_address);
                config["information"].AddKey(nameof(hdd_serial), hdd_serial);
                config["information"].AddKey(nameof(motherboard), motherboard);
                config["information"].AddKey(nameof(user_os), user_os);
                config["information"].AddKey(nameof(umamusume_file_path), umamusume_file_path);
            }

            foreach (var account in Accounts)
            {
                if (config.Sections.Any(x => x.SectionName == account.Name))
                {
                    config[account.Name]["login_secure_id"] = account.login_secure_id;
                    config[account.Name]["login_session_id"] = account.login_session_id;
                    config[account.Name]["savedata_file_path"] = account.savedata_file_path;
                    config[account.Name]["split_umamusume_file_path"] = account.split_umamusume_file_path;
                }
                else
                {
                    config.Sections.AddSection(account.Name);
                    config[account.Name].AddKey(nameof(account.login_secure_id), account.login_secure_id);
                    config[account.Name].AddKey(nameof(account.login_session_id), account.login_session_id);
                    config[account.Name].AddKey(nameof(account.savedata_file_path), account.savedata_file_path);
                    config[account.Name].AddKey(nameof(account.split_umamusume_file_path), account.split_umamusume_file_path);
                }
            }
            foreach (var removed in config.Sections.Where(x => x.SectionName != "information" && !Accounts.Any(y => y.Name == x.SectionName)))
            {
                config.Sections.RemoveSection(removed.SectionName);
            }

            parser.WriteFile(DMM_CONFIG_FILEPATH, config, Encoding.UTF8);
        }

        public class DMMAccount
        {
            public string Name { get; set; } = string.Empty;
            internal string split_umamusume_file_path { get; set; } = string.Empty;
            internal string savedata_file_path { get; set; } = string.Empty;
            internal string login_session_id { get; set; } = string.Empty;
            internal string login_secure_id { get; set; } = string.Empty;

            public void RunUmamusume()
            {
                AnsiConsole.Status().Start(I18N_Start_Checking, ctx =>
                {
                    var processes = Process.GetProcessesByName("umamusume");
                    AnsiConsole.MarkupLine(string.Format(I18N_Start_Checking_Log, string.Format(I18N_Start_Checking_Found, processes.Length)));
                    if (!processes.Any() || IgnoreExistProcess)
                    {
                        ctx.Spinner(Spinner.Known.BouncingBar);
                        ctx.Status(I18N_Start_GetToken);

                        using var proc = new Process(); //检查下载的文件是否正常
                        var dmmToken = string.Empty;
                        proc.StartInfo = new ProcessStartInfo
                        {
                            FileName = Environment.ProcessPath!,
                            Arguments = $"--get-dmm-onetime-token {Accounts.IndexOf(this)}",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        };
                        proc.Start();
                        while (!proc.StandardOutput.EndOfStream)
                        {
                            dmmToken = proc.StandardOutput.ReadLine();
                        }

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
                            var configFilepath = Path.Combine(Path.GetDirectoryName(umamusume_file_path)!, "config.json");
                            var config = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(File.ReadAllText(configFilepath))!;
                            if (string.IsNullOrEmpty(savedata_file_path))
                            {
                                if (config.ContainsKey("savedata_path"))
                                {
                                    config.Remove("savedata_path");
                                    File.WriteAllText(configFilepath, config.ToString(Newtonsoft.Json.Formatting.Indented));
                                }
                                Launch(dmmToken);
                            }
                            else
                            {
                                if (config.ContainsKey("savedata_path"))
                                {
                                    var prev = config["savedata_path"]!.ToString();
                                    config["savedata_path"] = savedata_file_path;
                                    File.WriteAllText(configFilepath, config.ToString(Newtonsoft.Json.Formatting.Indented));
                                    config["savedata_path"] = prev;
                                }
                                else
                                {
                                    config["savedata_path"] = savedata_file_path;
                                    File.WriteAllText(configFilepath, config.ToString(Newtonsoft.Json.Formatting.Indented));
                                    config.Remove("savedata_path");
                                }
                                Launch(dmmToken);
                                Server.OnPing.Wait(() => File.WriteAllText(configFilepath, config.ToString(Newtonsoft.Json.Formatting.Indented)));
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
            public async ValueTask<string?> GetExecuteArgsAsync()
            {
                var cookies = new CookieContainer();
                using var client = new HttpClient(new HttpClientHandler
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
                cookies.Add(new Cookie(nameof(login_session_id), login_session_id) { Domain = "apidgp-gameplayer.games.dmm.com" });
                cookies.Add(new Cookie(nameof(login_secure_id), login_secure_id) { Domain = "apidgp-gameplayer.games.dmm.com" });

                await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/gameinfo", new StringContent($"{{\"product_id\":\"umamusume\",\"game_type\":\"GCL\",\"game_os\":\"win\",\"mac_address\":\"{mac_address}\",\"hdd_serial\":\"{hdd_serial}\",\"motherboard\":\"{motherboard}\",\"user_os\":\"{user_os}\"}}", Encoding.UTF8, "application/json"));
                using var response = await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/launch/cl", new StringContent($"{{\"product_id\":\"umamusume\",\"game_type\":\"GCL\",\"game_os\":\"win\",\"launch_type\":\"LIB\",\"mac_address\":\"{mac_address}\",\"hdd_serial\":\"{hdd_serial}\",\"motherboard\":\"{motherboard}\",\"user_os\":\"{user_os}\"}}", Encoding.UTF8, "application/json"));
                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                if (json["result_code"]?.ToObject<int>() == 308)
                {
                    await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/agreement/confirm/client", new StringContent("{\"product_id\":\"umamusume\",\"is_notification\":false,\"is_myapp\":false}", Encoding.UTF8, "application/json"));
                    using var resp = await client.PostAsync("https://apidgp-gameplayer.games.dmm.com/v5/launch/cl", new StringContent($"{{\"product_id\":\"umamusume\",\"game_type\":\"GCL\",\"game_os\":\"win\",\"launch_type\":\"LIB\",\"mac_address\":\"{mac_address}\",\"hdd_serial\":\"{hdd_serial}\",\"motherboard\":\"{motherboard}\",\"user_os\":\"{user_os}\"}}", Encoding.UTF8, "application/json"));
                    json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                }
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
            void Launch(string args)
            {
                try
                {
                    using var Proc = new Process();
                    var StartInfo = new ProcessStartInfo
                    {
                        FileName = string.IsNullOrEmpty(split_umamusume_file_path) ? umamusume_file_path : split_umamusume_file_path,
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
            Version GetGameVersion()
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
                    } while (BitConverter.ToString(MD5.Create().ComputeHash(fs)).Replace("-", string.Empty).ToLower() != remote_hash);
                });
            }
        }
    }
}
