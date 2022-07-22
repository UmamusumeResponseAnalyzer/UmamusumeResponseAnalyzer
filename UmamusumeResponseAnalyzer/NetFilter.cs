using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetFilterAPI;
using Spectre.Console;

namespace UmamusumeResponseAnalyzer
{
    public static class NetFilter
    {
        private static readonly NFAPI nfAPI = new();
        async static Task Initialize()
        {
            try
            {
                var applicationDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer");
                var nfapiPath = Path.Combine(applicationDir, "nfapi.dll");
                var nfdriverPath = Path.Combine(applicationDir, "nfdriver.sys");
                var redirectorPath = Path.Combine(applicationDir, "Redirector.dll");
                if (!File.Exists(nfdriverPath) || !File.Exists(redirectorPath) || !File.Exists(nfapiPath))
                {
                    AnsiConsole.WriteLine("加速功能未启动：未找到加速驱动");
                    AnsiConsole.WriteLine("正在尝试重新下载加速驱动");
                    await ResourceUpdater.DownloadNetFilter(nfapiPath, nfdriverPath, redirectorPath);
                }
                NFAPI.SetDriverPath(nfdriverPath);
                Redirector.SetBinaryDirectory(applicationDir);
                NFAPI.EnableLog(false);
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine($"加速功能初始化失败: {ex.Message}");
            }
        }
        public static async Task Enable()
        {
            await Initialize();
            if (!Config.ContainsKey("PROXY_HOST") || !Config.ContainsKey("PROXY_PORT"))
            {
                AnsiConsole.WriteLine("加速功能未启动：未配置加速服务器");
                return;
            }
            var host = Config.Get<string>("PROXY_HOST");
            var port = int.Parse(Config.Get<string>("PROXY_PORT"));
            if (Config.ContainsKey("PROXY_USERNAME") || Config.ContainsKey("PROXY_PASSWORD"))
            {
                await nfAPI.StartAsync(host, port, new[] { "umamusume.exe", "UmamusumeResponseAnalyzer.exe" }, default!, (Config.Get<string>("PROXY_USERNAME"), Config.Get<string>("PROXY_PASSWORD")));
            }
            else
            {
                await nfAPI.StartAsync(host, port, new[] { "umamusume.exe", "UmamusumeResponseAnalyzer.exe" }, default!, default);
            }
        }
        public static async Task Disable() => await nfAPI.StopAsync();
        public static void InstallDriver() => NFAPI.InstallDriver();
        public static void UninstallDriver() => NFAPI.UninstallDriver();
    }
}
