using Spectre.Console;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using static UmamusumeResponseAnalyzer.Localization.ResourceUpdater;

namespace UmamusumeResponseAnalyzer
{
    public static class ResourceUpdater
    {
        static readonly string UPDATE_RECORD_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", ".update_record");
        static readonly ConcurrentBag<string> ETAG_RECORDS = [];
        static ResourceUpdater()
        {
            if (File.Exists(UPDATE_RECORD_FILEPATH))
            {
                ETAG_RECORDS = new(File.ReadAllLines(UPDATE_RECORD_FILEPATH));
            }
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
            else if (exist && !(MD5.HashData(File.ReadAllBytes(Environment.ProcessPath!)).SequenceEqual(MD5.HashData(File.ReadAllBytes(path))))) //临时目录与当前目录的不一致则认为未更新
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
            File.WriteAllLines(UPDATE_RECORD_FILEPATH, ETAG_RECORDS); // 检测通过，更新etag记录
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
            File.WriteAllLines(UPDATE_RECORD_FILEPATH, ETAG_RECORDS);
            AnsiConsole.MarkupLine(I18N_DownloadedInstruction);
            Console.ReadKey();
        }
        public static async Task UpdateProgram()
        {
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
            if (File.Exists(path))
            {
                var selfSHA256 = SHA256.HashData(File.ReadAllBytes(Environment.ProcessPath!));
                var targetSHA256 = SHA256.HashData(File.ReadAllBytes(path));
                if (selfSHA256.SequenceEqual(targetSHA256))
                {
                    Console.WriteLine(I18N_AlreadyLatestInstruction);
                }
                else
                {
                    Console.WriteLine(I18N_BeginUpdateProgramInstruction);
                    Console.ReadKey();
                    CloseToUpdate();
                }
            }
            Console.WriteLine(Localization.LaunchMenu.I18N_Options_BackToMenuInstruction);
            Console.ReadKey();
        }
        public static async Task DownloadUraCore(string path)
        {
            if (!File.Exists(path))
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
                        await Download(ctx, I18N_DownloadingUraCore, path);
                    });
            }
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
        public static async Task DownloadCmder(string cmderPath)
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
                    var zipPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "cmder.zip");
                    var download = Download(ctx, I18N_DownloadingCmder, zipPath);
                    var unzip = ctx.AddTask(I18N_DecompressCmder, false).IsIndeterminate();
                    await download;
                    unzip.StartTask();
                    unzip.IsIndeterminate(false);
                    System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, cmderPath);
                    File.Delete(zipPath);
                    unzip.StopTask();
                });
        }
        static string GetDownloadUrl(string filepath)
        {
            const string CNHost = "https://assets.shuise.net/UmamusumeResponseAnalyzer";
            const string GithubHost = "https://raw.githubusercontent.com/EtherealAO/UmamusumeResponseAnalyzer/master";
            const string OSSHost = "https://assets.shuise.net/URA";
            var isCN = RegionInfo.CurrentRegion.Name == "CN" || CultureInfo.CurrentUICulture.Name == "zh-CN";
            var ext = Path.GetExtension(filepath);
            var filename = Path.GetFileName(filepath);
            switch (filename)
            {
                case var _ when filename.Contains("UmamusumeResponseAnalyzer.exe"):
                    filename = "UmamusumeResponseAnalyzer.exe";
                    break;
                case var _ when filename == "nfapi.dll":
                    return OSSHost + "/nfapi.dll";
                case var _ when filename == "nfdriver.sys":
                    return OSSHost + "/nfdriver.sys";
                case var _ when filename == "Redirector.dll":
                    return OSSHost + "/Redirector.dll";
                case var _ when filename == "cmder.zip":
                    return OSSHost + "/cmder.zip";
            }
            var host = !Config.Get(Localization.Config.I18N_ForceUseGithubToUpdate) && isCN ? CNHost : GithubHost;
            var i18n = Thread.CurrentThread.CurrentUICulture.Name switch
            {
                "zh-CN" => "zh-CN/",
                _ => string.Empty
            };
            return ext switch
            {
                ".json" => $"{host}/GameData/{i18n}{filename}",
                ".br" => $"{host}/GameData/{i18n}{filename}",
                ".exe" => !Config.Get(Localization.Config.I18N_ForceUseGithubToUpdate) && isCN ? $"{host}/{filename}" : $"https://github.com/UmamusumeResponseAnalyzer/UmamusumeResponseAnalyzer/releases/latest/download/UmamusumeResponseAnalyzer.exe"
            };
        }
        public static async Task Download(ProgressContext ctx = null!, string instruction = null!, string path = null!)
        {
            var downloadURL = GetDownloadUrl(path);
            var client = new HttpClient()
            {
                DefaultRequestVersion = HttpVersion.Version20
            };

            #region 检测更新服务器是否可用
            try
            {
                await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, downloadURL));
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

            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, downloadURL), HttpCompletionOption.ResponseHeadersRead);
            var task = ctx?.AddTask(instruction, false);
            task?.MaxValue(response.Content.Headers.ContentLength ?? 0);
            task?.StartTask();
            if (task != null && response.Content.Headers.TryGetValues("ETag", out var etags))
            {
                var etag = string.Join(string.Empty, etags);
                if (ETAG_RECORDS.Contains(etag)) // Etag已下载过，所以是最新的。这么做的话第一次更新必定会下载所有文件，包括程序
                {
                    task.Increment(response.Content.Headers.ContentLength ?? 0);
                    return;
                }
                else
                {
                    ETAG_RECORDS.Add(etag);
                }
            }

            response = await client.GetAsync(downloadURL);

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
