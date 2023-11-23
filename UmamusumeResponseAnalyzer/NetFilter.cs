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
        static readonly string applicationDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer");
        static readonly string nfapiPath = Path.Combine(applicationDir, "nfapi.dll");
        static readonly string nfdriverPath = Path.Combine(applicationDir, "nfdriver.sys");
        static readonly string redirectorPath = Path.Combine(applicationDir, "Redirector.dll");
        static NetFilter()
        {
            try
            {
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
            try
            {
                if (!File.Exists($"{Environment.SystemDirectory}\\drivers\\netfilter2.sys"))
                {
                    AnsiConsole.MarkupLine("加速功能未启动：NetFilter驱动未安装");
                    return;
                }
                if (!Config.ContainsKey("加速服务器地址") || !Config.ContainsKey("加速服务器端口") || !Config.ContainsKey("加速服务器类型"))
                {
                    AnsiConsole.WriteLine("加速功能未启动：未配置加速服务器");
                    return;
                }
                NFAPI.Host = Config.Get<string>("加速服务器地址") ?? string.Empty;
                NFAPI.Port = int.Parse(Config.Get<string>("加速服务器端口") ?? string.Empty);
                NFAPI.HandleList = ["umamusume.exe", "UmamusumeResponseAnalyzer.exe", "Nox.exe", "NoxVMHandle.exe", "NoxVMSVC.exe"];

                if (Config.Get<string>("加速服务器类型") == "http")
                {
                    await NFAPI.StartAsync(true);
                }
                else
                {
                    if (Config.TryGetValue("加速服务器用户名", out var username) && Config.TryGetValue("加速服务器密码", out var password) && username is not null && password is not null)
                    {
                        await NFAPI.StartAsync(false, (string)username.Value, (string)password.Value);
                    }
                    else
                    {
                        await NFAPI.StartAsync();
                    }
                }
            }
            catch
            {
                AnsiConsole.MarkupLine("[red]NetFilter服务启动时出现错误，请检查相关设置[/]");
            }
        }
        public static async Task Disable() => await NFAPI.StopAsync();
        public static void InstallDriver() => NFAPI.InstallDriver();
        public static void UninstallDriver() => NFAPI.UninstallDriver();
    }
}
