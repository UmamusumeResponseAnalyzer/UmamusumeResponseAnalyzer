using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using UmamusumeResponseAnalyzer.Plugin;
using static UmamusumeResponseAnalyzer.Localization.ResourceUpdater;

namespace UmamusumeResponseAnalyzer
{
    public static class ResourceUpdater
    {
        public static HttpClient HttpClient = new()
        {
            DefaultRequestHeaders =
            {
                UserAgent = { new System.Net.Http.Headers.ProductInfoHeaderValue("UmamusumeResponseAnalyzer", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown Version") }
            }
        };
        public static async Task<IEnumerable<PluginInformation>> GetPluginsFromRepository(string repositoryUrl)
        {
            var jsonText = await HttpClient.GetStringAsync(repositoryUrl);
            var plugins = JsonConvert.DeserializeObject<IEnumerable<PluginInformation>>(jsonText);
            return plugins ?? [];
        }
        public static async Task<bool> NeedUpdate()
        {
            var json = JObject.Parse(await HttpClient.GetStringAsync("https://api.github.com/repos/UmamusumeResponseAnalyzer/UmamusumeResponseAnalyzer/releases/latest"));
            var latestVersion = json["tag_name"]?.ToString() ?? string.Empty;
            return !latestVersion.Equals("v" + Assembly.GetExecutingAssembly().GetName().Version);
        }
        public static async Task UpdateProgram()
        {
            if (!await NeedUpdate())
            {
                Console.WriteLine(I18N_AlreadyLatestInstruction);
                Console.WriteLine(Localization.LaunchMenu.I18N_Options_BackToMenuInstruction);
                Console.ReadKey();
                return;
            }
            var path = Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe");
            await AnsiConsole.Progress()
                .Columns(
                [
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn()
                ])
                .StartAsync(async ctx =>
                {
                    var tasks = new List<Task>();

                    var programTask = Download(ctx, I18N_DownloadProgramInstruction, path);
                    tasks.Add(programTask);

                    await Task.WhenAll(tasks);
                });
            Console.WriteLine(I18N_BeginUpdateProgramInstruction);
            Console.ReadKey();
            CloseToUpdate();
        }
        public static void CloseToUpdate()
        {
            using (var proc = new Process()) //检查下载的文件是否正常
            {
                var output = string.Empty;
                try
                {
                    proc.StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe"),
                        Arguments = $"-v",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    proc.Start();
                    while (!proc.StandardOutput.EndOfStream)
                    {
                        output = proc.StandardOutput.ReadLine();
                    }
                }
                catch
                {
                }
                if (string.IsNullOrEmpty(output))
                {
                    AnsiConsole.MarkupLine(I18N_UpdatedFileCorrupted);
                    File.Delete(Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe"));
                    return;
                }
            }
            using var Proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe"),
                    Arguments = $"--update \"{Environment.ProcessPath}\"",
                    UseShellExecute = true
                }
            };
            Proc.Start();
            Environment.Exit(0);
        }
        public static async Task TryUpdateProgram(string savepath = null!)
        {
            var path = Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe");
            var exist = File.Exists(path);
            if (!string.IsNullOrEmpty(savepath))
            {
                path = savepath;
                File.Copy(Environment.ProcessPath!, path, true);

                using var Proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    }
                };
                Proc.Start(); //把新程序复制到原来的目录后就启动
                Environment.Exit(0);
            }
            else if (exist && !SHA256.HashData(File.ReadAllBytes(Environment.ProcessPath!)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))) //临时目录与当前目录的不一致则认为未更新
            {
                CloseToUpdate();
                exist = false; //能执行到这就代表更新文件受损，已经被删掉了
            }

            if (exist) //删除临时文件
            {
                File.Delete(path);
                await UpdateAssets(); //既然有临时文件，那必然是刚更新过的，再执行一次更新保证数据即时
                AnsiConsole.Clear();
            }
        }
        public static async Task UpdateAssets()
        {
            await AnsiConsole.Progress()
                .Columns(
                [
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn()
                ])
                .StartAsync(async ctx =>
                {
                    var tasks = new List<Task>();

                    var eventTask = Download(ctx, I18N_DownloadEventsInstruction, Database.EVENT_NAME_FILEPATH);
                    tasks.Add(eventTask);

                    var successEventTask = Download(ctx, I18N_DownloadSuccessEventsInstruction, Database.SUCCESS_EVENT_FILEPATH);
                    tasks.Add(successEventTask);

                    var namesTask = Download(ctx, I18N_DownloadNamesInstruction, Database.NAMES_FILEPATH);
                    tasks.Add(namesTask);

                    var skillTask = Download(ctx, I18N_DownloadSkillDataInstruction, Database.SKILLS_FILEPATH);
                    tasks.Add(skillTask);

                    var talentSkillTask = Download(ctx, I18N_DownloadTalentSkillInstruction, Database.TALENT_SKILLS_FILEPATH);
                    tasks.Add(talentSkillTask);

                    var factorIdTask = Download(ctx, I18N_DownloadFactorIdsInstruction, Database.FACTOR_IDS_FILEPATH);
                    tasks.Add(factorIdTask);

                    var skillUpgradeSpecialityTask = Download(ctx, I18N_DownloadSkillUpgradeSpecialityInstruction, Database.SKILL_UPGRADE_SPECIALITY_FILEPATH);
                    tasks.Add(skillUpgradeSpecialityTask);

                    await Task.WhenAll(tasks);
                });
            AnsiConsole.MarkupLine(I18N_DownloadedInstruction);
            Console.ReadKey();
        }
        public static async Task DownloadNetFilter(string nfapiPath, string nfdriverPath, string redirectorPath)
        {
            if (!File.Exists(nfapiPath) || !File.Exists(nfdriverPath) || !File.Exists(redirectorPath))
            {
                await AnsiConsole.Progress()
                    .Columns(
                    [
                            new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new RemainingTimeColumn(),
                        new SpinnerColumn()
                    ])
                    .StartAsync(async ctx =>
                    {
                        var tasks = new List<Task>();

                        var nfAPITask = Download(ctx, I18N_DownloadingNFApi, nfapiPath);
                        tasks.Add(nfAPITask);

                        var nfDriverTask = Download(ctx, I18N_DownloadingNFDriver, nfdriverPath);
                        tasks.Add(nfDriverTask);

                        var redirectorTask = Download(ctx, I18N_DownloadingRedirector, redirectorPath);
                        tasks.Add(redirectorTask);

                        await Task.WhenAll(tasks);
                    });
            }
        }
        static string GetDownloadUrl(string filepath)
        {
            var ProgramUrl = "https://github.com/UmamusumeResponseAnalyzer/UmamusumeResponseAnalyzer/releases/latest/download/UmamusumeResponseAnalyzer.exe".AllowMirror();
            var GithubHost = string.IsNullOrEmpty(Config.Updater.CustomDatabaseRepository) ? "https://github.com/UmamusumeResponseAnalyzer/Assets/raw/refs/heads/main/".AllowMirror() : Config.Updater.CustomDatabaseRepository;
            var OSSHost = "https://assets.shuise.net/URA";
            var ext = Path.GetExtension(filepath);
            var filename = Path.GetFileName(filepath);
            switch (filename)
            {
                case var _ when filename.Contains("UmamusumeResponseAnalyzer.exe"):
                    filename = "UmamusumeResponseAnalyzer.exe";
                    break;
                case "nfapi.dll":
                    return OSSHost + "/nfapi.dll";
                case "nfdriver.sys":
                    return OSSHost + "/nfdriver.sys";
                case "Redirector.dll":
                    return OSSHost + "/Redirector.dll";
            }
            return ext switch
            {
                ".br" => $"{GithubHost}/GameData/{Config.Updater.DatabaseLanguage}/{filename}",
                ".exe" => ProgramUrl
            };
        }
        public static async Task Download(ProgressContext ctx = null!, string instruction = null!, string path = null!)
        {
            var downloadURL = GetDownloadUrl(path);
            #region 检测更新服务器是否可用
            try
            {
                await HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, downloadURL));
            }
            catch
            {
                if (new Uri(downloadURL).Host == "raw.githubusercontent.com")
                {
                    AnsiConsole.MarkupLine(I18N_AccessGithubFail, downloadURL);
                }
                else
                {
                    AnsiConsole.MarkupLine(I18N_AccessMirrorFail, downloadURL);
                }
                return;
            }
            #endregion

            var response = await HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, downloadURL), HttpCompletionOption.ResponseHeadersRead);
            var task = ctx?.AddTask(instruction, false);
            task?.MaxValue(response.Content.Headers.ContentLength ?? 0);
            task?.StartTask();

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            var buffer = new byte[8192];
            while (true)
            {
                var read = await contentStream.ReadAsync(buffer);
                if (read == 0)
                    break;
                task?.Increment(read);
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
            }
        }
    }
}
