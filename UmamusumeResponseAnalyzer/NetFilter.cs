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
                }
                else
                {
                    AnsiConsole.WriteLine("未找到加速驱动，加速功能启动失败");
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
            if (!Config.ContainsKey("PROXY_HOST") || !Config.ContainsKey("PROXY_PORT"))
            {
                AnsiConsole.WriteLine("未配置加速服务器，加速功能启动失败");
                return;
            }
            if (Config.ContainsKey("PROXY_USERNAME") || Config.ContainsKey("PROXY_PASSWORD"))
            {
                await nfAPI.StartAsync(Config.Get<string>("PROXY_HOST"), int.Parse(Config.Get<string>("PROXY_PORT")), new[] { "umamusume.exe", "UmamusumeResponseAnalyzer.exe" }, default!, (Config.Get<string>("PROXY_USERNAME"), Config.Get<string>("PROXY_PASSWORD")));
            }
            else
            {
                await nfAPI.StartAsync(Config.Get<string>("PROXY_HOST"), int.Parse(Config.Get<string>("PROXY_PORT")), new[] { "umamusume.exe", "UmamusumeResponseAnalyzer.exe" }, default!, default);
            }
        }
        public static async Task Disable() => await nfAPI.StopAsync();
        public static void InstallDriver() => NFAPI.InstallDriver();
        public static void UninstallDriver() => NFAPI.UninstallDriver();
    }
}
