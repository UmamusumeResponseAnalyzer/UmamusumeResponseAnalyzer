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
        static NetFilter()
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
                    ResourceUpdater.DownloadNetFilter(nfapiPath, nfdriverPath, redirectorPath).Wait();
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
            if (!File.Exists($"{Environment.SystemDirectory}\\drivers\\netfilter2.sys"))
                return;
            if (!Config.ContainsKey("加速服务器地址") || !Config.ContainsKey("加速服务器端口"))
            {
                AnsiConsole.WriteLine("加速功能未启动：未配置加速服务器");
                return;
            }
            var host = Config.Get<string>("加速服务器地址");
            var port = int.Parse(Config.Get<string>("加速服务器端口"));
            var apps = new[] { "umamusume.exe", "UmamusumeResponseAnalyzer.exe", "Nox.exe", "NoxVMHandle.exe", "NoxVMSVC.exe" };
            if (Config.ContainsKey("加速服务器用户名") || Config.ContainsKey("加速服务器密码"))
            {
                var username = Config.Get<string>("加速服务器用户名");
                var password = Config.Get<string>("加速服务器密码");
                if (!(string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)))
                    await nfAPI.StartAsync(host, port, apps, default!, (username, password));
                else
                    await nfAPI.StartAsync(host, port, apps, default!, default);
            }
            else
            {
                await nfAPI.StartAsync(host, port, apps, default!, default);
            }
        }
        public static async Task Disable() => await nfAPI.StopAsync();
        public static void InstallDriver() => NFAPI.InstallDriver();
        public static void UninstallDriver() => NFAPI.UninstallDriver();
    }
}
