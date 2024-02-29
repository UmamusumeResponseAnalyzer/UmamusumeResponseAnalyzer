using NetFilterAPI;
using Spectre.Console;
using static UmamusumeResponseAnalyzer.Localization.Config;
using static UmamusumeResponseAnalyzer.Localization.NetFilter;

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
                    AnsiConsole.WriteLine(I18N_NFDriver_NotFoundRedownload);
                    ResourceUpdater.DownloadNetFilter(nfapiPath, nfdriverPath, redirectorPath).Wait();
                }
                NFAPI.SetDriverPath(nfdriverPath);
                Redirector.SetBinaryDirectory(applicationDir);
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
                if (!Config.ContainsKey(I18N_ProxyHost) || !Config.ContainsKey(I18N_ProxyPort) || !Config.ContainsKey(I18N_ProxyServerType))
                {
                    AnsiConsole.WriteLine(I18N_ProxyServerNotConfigure);
                    return;
                }
                NFAPI.Host = Config.Get<string>(I18N_ProxyHost) ?? string.Empty;
                NFAPI.Port = int.Parse(Config.Get<string>(I18N_ProxyPort) ?? string.Empty);
                NFAPI.HandleList = ["umamusume.exe", "UmamusumeResponseAnalyzer.exe", "Nox.exe", "NoxVMHandle.exe", "NoxVMSVC.exe"];

                if (Config.Get<string>(I18N_ProxyServerType) == "http")
                {
                    await NFAPI.StartAsync(true);
                }
                else
                {
                    if (Config.TryGetValue(I18N_ProxyUsername, out var username) && Config.TryGetValue(I18N_ProxyPassword, out var password) && username is not null && password is not null)
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
                AnsiConsole.MarkupLine(I18N_ProxyServerConfigureError);
            }
        }
        public static async Task Disable() => await NFAPI.StopAsync();
        public static void InstallDriver() => NFAPI.InstallDriver();
        public static void UninstallDriver() => NFAPI.UninstallDriver();
    }
}
