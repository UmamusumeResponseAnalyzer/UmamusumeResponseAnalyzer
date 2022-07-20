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
        private static NFAPI nfAPI = new();
        private static bool initialized = false;
        static NetFilter()
        {
            try
            {
                var nfDriver = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "nfdriver.sys");
                var binaryDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer");
                if (File.Exists(nfDriver))
                {
                    NFAPI.SetDriverPath(nfDriver);
                    Redirector.SetBinaryDirectory(binaryDirectory);
                    NFAPI.EnableLog(false);
                    initialized = true;
                }
                else
                {
                    AnsiConsole.WriteLine("加速功能未启动：未找到加速驱动");
                    return;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine($"加速功能初始化失败: {ex.Message}");
            }
        }
        public static async Task Enable()
        {
            if (!initialized)
            {
                AnsiConsole.WriteLine("加速功能未启动：初始化失败");
                return;
            }
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
