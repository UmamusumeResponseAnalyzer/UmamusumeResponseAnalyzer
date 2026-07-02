using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using UmamusumeResponseAnalyzer.LiveDisplay;
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
                LiveDisplayConsole.WriteLine(I18N_AlreadyLatestInstruction);
                LiveDisplayConsole.WriteLine(Localization.LaunchMenu.I18N_Options_BackToMenuInstruction);
                LiveDisplayConsole.ReadKey();
                return;
            }
            var path = Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe");
            try
            {
                await LiveDisplayConsole.RunProgressAsync(p =>
                    p.Columns(
                    [
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new RemainingTimeColumn(),
                        new SpinnerColumn()
                    ])
                    .StartAsync(async ctx =>
                    {
                        await Download(ctx, I18N_DownloadProgramInstruction, path);
                    }));
            }
            catch (Exception ex)
            {
                LiveDisplayConsole.WriteException(ex);
                LiveDisplayConsole.ReadKey();
                return;
            }

            LiveDisplayConsole.WriteLine(I18N_BeginUpdateProgramInstruction);
            LiveDisplayConsole.ReadKey();
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
                    LiveDisplayConsole.MarkupLine(I18N_UpdatedFileCorrupted);
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
                return;
            }
            else if (exist && !FilesHaveSameHash(Environment.ProcessPath!, path)) //临时目录与当前目录的不一致则认为未更新
            {
                CloseToUpdate();
                exist = false; //能执行到这就代表更新文件受损，已经被删掉了
            }

            if (exist) //删除临时文件
            {
                File.Delete(path);
                await UpdateAssets();
                LiveDisplayConsole.Clear();
            }
        }
        static bool FilesHaveSameHash(string leftPath, string rightPath)
        {
            using var left = File.OpenRead(leftPath);
            using var right = File.OpenRead(rightPath);
            return SHA256.HashData(left).SequenceEqual(SHA256.HashData(right));
        }
        public static async Task UpdateAssets()
        {
            try
            {
                await LiveDisplayConsole.RunProgressAsync(p =>
                    p.Columns(
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

                        var winSaddleTask = Download(ctx, Database.SADDLE_IDS_FILEPATH, Database.SADDLE_IDS_FILEPATH);
                        tasks.Add(winSaddleTask);

                        var successionRelationTask = Download(ctx, Database.SUCCESSION_RELATION_FILEPATH, Database.SUCCESSION_RELATION_FILEPATH);
                        tasks.Add(successionRelationTask);

                        await Task.WhenAll(tasks);
                    }));
            }
            catch (Exception ex)
            {
                LiveDisplayConsole.WriteException(ex);
                LiveDisplayConsole.ReadKey();
                return;
            }

            LiveDisplayConsole.MarkupLine(I18N_DownloadedInstruction);
            LiveDisplayConsole.ReadKey();
        }
        static string GetDownloadUrl(string filepath)
        {
            var ProgramUrl = "https://github.com/UmamusumeResponseAnalyzer/UmamusumeResponseAnalyzer/releases/latest/download/UmamusumeResponseAnalyzer.exe".AllowMirror();
            var GithubHost = string.IsNullOrEmpty(Config.Updater.CustomDatabaseRepository) ? "https://github.com/UmamusumeResponseAnalyzer/Assets/raw/refs/heads/main/".AllowMirror() : Config.Updater.CustomDatabaseRepository;
            var ext = Path.GetExtension(filepath);
            var filename = Path.GetFileName(filepath);
            return ext switch
            {
                ".br" => $"{GithubHost}/GameData/{Config.Updater.DatabaseLanguage}/{filename}",
                ".exe" => ProgramUrl
            };
        }
        public static async Task Download(ProgressContext ctx = null!, string instruction = null!, string path = null!)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("下载目标路径不能为空。", nameof(path));

            var downloadURL = GetDownloadUrl(path);
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath)!;
            var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            ProgressTask? task = null;
            try
            {
                using var response = await HttpClient.GetAsync(downloadURL, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                task = ctx?.AddTask(instruction, false);
                task?.MaxValue(response.Content.Headers.ContentLength ?? 0);
                task?.StartTask();

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                {
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

                File.Move(tempPath, fullPath, overwrite: true);
            }
            catch (Exception) when (new Uri(downloadURL).Host == "raw.githubusercontent.com")
            {
                LiveDisplayConsole.MarkupLine(I18N_AccessGithubFail, downloadURL.EscapeMarkup());
                throw;
            }
            catch
            {
                LiveDisplayConsole.MarkupLine(I18N_AccessMirrorFail, downloadURL.EscapeMarkup());
                throw;
            }
            finally
            {
                // 下载失败也必须结束 task，否则 Spectre Progress 会一直渲染卡住的进度条
                if (task is { IsFinished: false })
                    task.StopTask();
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
