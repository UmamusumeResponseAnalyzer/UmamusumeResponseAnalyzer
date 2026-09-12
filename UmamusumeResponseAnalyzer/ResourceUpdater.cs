using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using UmamusumeResponseAnalyzer.TerminalGui;
using static UmamusumeResponseAnalyzer.Localization.ResourceUpdater;

namespace UmamusumeResponseAnalyzer
{
    public static class ResourceUpdater
    {
        internal static HttpClient HttpClient { get; set; } = new()
        {
            DefaultRequestHeaders =
            {
                UserAgent = { new System.Net.Http.Headers.ProductInfoHeaderValue("UmamusumeResponseAnalyzer", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown Version") }
            }
        };
        internal static async Task<bool> NeedUpdate(CancellationToken cancellationToken = default)
        {
            var json = JObject.Parse(await HttpClient.GetStringAsync(
                "https://api.github.com/repos/UmamusumeResponseAnalyzer/UmamusumeResponseAnalyzer/releases/latest",
                cancellationToken));
            var latestVersion = json["tag_name"]?.ToString() ?? string.Empty;
            return !latestVersion.Equals("v" + Assembly.GetExecutingAssembly().GetName().Version);
        }
        internal static async Task UpdateProgram(CancellationToken cancellationToken = default)
        {
            if (!await NeedUpdate(cancellationToken))
            {
                TerminalUi.Acknowledge(I18N_AlreadyLatestInstruction, cancellationToken);
                return;
            }

            var path = Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe");
            await TerminalUi.RunProgressAsync(
                (progress, token) => Download(
                    progress,
                    I18N_DownloadProgramInstruction,
                    path,
                    token),
                cancellationToken);

            if (!TerminalUi.Acknowledge(
                    I18N_BeginUpdateProgramInstruction,
                    cancellationToken))
                return;

            LaunchDownloadedProgram(path);
        }

        private static void LaunchDownloadedProgram(string path)
        {
            using (var proc = new Process()) //检查下载的文件是否正常
            {
                var output = string.Empty;
                try
                {
                    proc.StartInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "-v",
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
                    TerminalUi.Log("URA", I18N_UpdatedFileCorrupted, UiSeverity.Error);
                    TerminalUi.Notify("URA", I18N_UpdatedFileCorrupted, UiSeverity.Error);
                    File.Delete(path);
                    return;
                }
            }
            UmamusumeResponseAnalyzer.StartAfterTerminalCleanup(new ProcessStartInfo
            {
                FileName = path,
                Arguments = $"--update \"{Environment.ProcessPath}\"",
                UseShellExecute = true
            });
        }
        public static async Task HandleStartupProgramUpdateAsync(CancellationToken cancellationToken)
        {
            var path = Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe");
            if (!File.Exists(path))
                return;

            if (!FilesHaveSameHash(Environment.ProcessPath!, path))
            {
                LaunchDownloadedProgram(path);
                return;
            }

            File.Delete(path);
            await UpdateAssets(cancellationToken);
        }

        public static void InstallProgramUpdate(string savePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(savePath);
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定当前程序路径，不能安装更新。");
            var fullSavePath = Path.GetFullPath(savePath);
            File.Copy(processPath, fullSavePath, true);
            UmamusumeResponseAnalyzer.StartAfterTerminalCleanup(new ProcessStartInfo
            {
                FileName = fullSavePath,
                UseShellExecute = true
            });
        }
        static bool FilesHaveSameHash(string leftPath, string rightPath)
        {
            using var left = File.OpenRead(leftPath);
            using var right = File.OpenRead(rightPath);
            return SHA256.HashData(left).SequenceEqual(SHA256.HashData(right));
        }
        internal static async Task UpdateAssets(CancellationToken cancellationToken = default)
        {
            await TerminalUi.RunProgressAsync((progress, token) => Task.WhenAll(
                [
                    Download(progress, I18N_DownloadEventsInstruction, Database.EVENT_NAME_FILEPATH, token),
                    Download(progress, I18N_DownloadNamesInstruction, Database.NAMES_FILEPATH, token),
                    Download(progress, I18N_DownloadSkillDataInstruction, Database.SKILLS_FILEPATH, token),
                    Download(progress, I18N_DownloadTalentSkillInstruction, Database.TALENT_SKILLS_FILEPATH, token),
                    Download(progress, I18N_DownloadFactorIdsInstruction, Database.FACTOR_IDS_FILEPATH, token),
                    Download(progress, I18N_DownloadSkillUpgradeSpecialityInstruction, Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, token),
                    Download(progress, Database.SADDLE_IDS_FILEPATH, Database.SADDLE_IDS_FILEPATH, token),
                    Download(progress, Database.SUCCESSION_RELATION_FILEPATH, Database.SUCCESSION_RELATION_FILEPATH, token)
                ]),
                cancellationToken);

            TerminalUi.Acknowledge(I18N_DownloadedInstruction, cancellationToken);
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
        internal static async Task Download(
            IProgress<DownloadProgress>? progress = null,
            string? instruction = null,
            string? path = null,
            CancellationToken cancellationToken = default,
            string? downloadUrl = null,
            long? expectedLength = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("下载目标路径不能为空。", nameof(path));

            var downloadURL = downloadUrl ?? GetDownloadUrl(path);
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath)!;
            var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using var response = await HttpClient.GetAsync(
                    downloadURL,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                if (expectedLength is { } expected && response.Content.Headers.ContentLength is { } actual && actual != expected)
                    throw new InvalidDataException($"Download length mismatch: expected {expected}, received {actual}.");
                var total = expectedLength ?? response.Content.Headers.ContentLength ?? 0;
                long completed = 0;
                progress?.Report(new(
                    fullPath,
                    instruction ?? Path.GetFileName(path),
                    completed,
                    total));

                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    while (true)
                    {
                        var read = await contentStream.ReadAsync(buffer, cancellationToken);
                        if (read == 0)
                            break;
                        completed += read;
                        if (expectedLength is { } limit && completed > limit)
                            throw new InvalidDataException($"Download exceeds the expected {limit} bytes.");
                        progress?.Report(new(
                            fullPath,
                            instruction ?? Path.GetFileName(path),
                            completed,
                            total));
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }

                if (expectedLength is { } length && completed != length)
                    throw new InvalidDataException($"Download length mismatch: expected {length}, received {completed}.");
                File.Move(tempPath, fullPath, overwrite: true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (downloadUrl is null && new Uri(downloadURL).Host == "raw.githubusercontent.com")
            {
                TerminalUi.Log("URA", string.Format(I18N_AccessGithubFail, downloadURL));
                throw;
            }
            catch (Exception) when (downloadUrl is null)
            {
                TerminalUi.Log("URA", string.Format(I18N_AccessMirrorFail, downloadURL));
                throw;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }

    internal sealed record DownloadProgress(
        string Id,
        string Description,
        long Completed,
        long Total);
}
