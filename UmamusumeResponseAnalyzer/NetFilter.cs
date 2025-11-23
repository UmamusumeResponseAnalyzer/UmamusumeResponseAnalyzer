using NetFilterAPI;
using Spectre.Console;
using static UmamusumeResponseAnalyzer.Localization.NetFilter;

namespace UmamusumeResponseAnalyzer
{
    public static class NetFilter
    {
        public static readonly string nfapiPath = "nfapi.dll";
        public static readonly string nfdriverPath = "nfdriver.sys";
        public static readonly string redirectorPath = "Redirector.dll";
        static NetFilter()
        {
            try
            {
                if (!File.Exists(nfdriverPath) || !File.Exists(redirectorPath) || !File.Exists(nfapiPath))
                {
                    AnsiConsole.WriteLine(I18N_NFDriver_NotFoundRedownload);
                    ResourceUpdater.DownloadNetFilter(nfapiPath, nfdriverPath, redirectorPath).Wait();
                }
                NFAPI.SetDriverPath(nfdriverPath);
                Redirector.SetBinaryDirectory("./");
                NFAPI.EnableLog(false);
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine(string.Format(I18N_NFDriver_StartFail, ex.Message));
            }
        }
        public static async Task Enable()
        {
            try
            {
                if (!File.Exists($"{Environment.SystemDirectory}\\drivers\\netfilter2.sys"))
                {
                    AnsiConsole.MarkupLine(I18N_NFDriver_NotInstall);
                    return;
                }
                if (string.IsNullOrEmpty(Config.NetFilter.Host) || Config.NetFilter.Port == 0)
                {
                    AnsiConsole.WriteLine(I18N_ProxyServerNotConfigure);
                    return;
                }
                NFAPI.Host = Config.NetFilter.Host ?? string.Empty;
                NFAPI.Port = Config.NetFilter.Port;
                NFAPI.HandleList = ["umamusume.exe", "UmamusumeResponseAnalyzer.exe", "Nox.exe", "NoxVMHandle.exe", "NoxVMSVC.exe"];

                if (Config.NetFilter.ServerType == "http")
                {
                    await NFAPI.StartAsync(true);
                }
                else
                {
                    if (!string.IsNullOrEmpty(Config.NetFilter.Username))
                    {
                        await NFAPI.StartAsync(false, Config.NetFilter.Username, Config.NetFilter.Password);
                    }
                    else
                    {
                        await NFAPI.StartAsync();
                    }
                }
            }
            catch
            {
                AnsiConsole.MarkupLine(I18N_ProxyServerConfigureError);
            }
        }
        public static async Task Disable() => await NFAPI.StopAsync();
        public static void InstallDriver() => NFAPI.InstallDriver();
        public static void UninstallDriver() => NFAPI.UninstallDriver();
    }
}
