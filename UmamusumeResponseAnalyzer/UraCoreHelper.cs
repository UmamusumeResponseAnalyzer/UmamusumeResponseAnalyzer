using Microsoft.Win32;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer
{
    public static class UraCoreHelper
    {
        public static readonly string[] uraCoreHashs =
        {
            "120046C0354DCF01C3B2EC71B99ED766DD90D87E49FBDECF3CCCDCDC43DDBD98", //1.2.4
            "9CF7F9F8CE0769F79F9446917DE451E15D1E7B14461142A32F19ECC10BDCDCC3", //1.2.3
            "97E1BD395E24DB2D6B67524871F67FAA32F03578433A21884366531974177109", //1.2.2
            "3E781DE5D6CF4F0DAEC92A48C542A17631B001E89F3B8B91A7EA324AC208A4A3", //1.2.1
            "C41770D8F0A2C8B0A437EC5585A9E8C8D98D4C4CEA396ABE49342168263BE075", //1.2.0
            "195028C53FB1586987A9A981D948FF47521D5249DC7C3D04252CA5751CA7DBBD", //1.1.0
            "30FD85DB47ADCA52093CB4D4C14F398874321A8EAA1CEE4A408CED4113D66930", //1.0.0
        };
        private static List<string> gamePaths = new();
        public static List<string> GamePaths
        {
            get
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || gamePaths.Any()) return gamePaths;
                var _gamePaths = new List<string>();
                try
                {
                    var muiCache = Registry.CurrentUser.OpenSubKey("Software")?.OpenSubKey("Classes")?.OpenSubKey("Local Settings")?.OpenSubKey("Software")?.OpenSubKey("Microsoft")?.OpenSubKey("Windows")?.OpenSubKey("Shell")?.OpenSubKey("MuiCache");
                    if (muiCache != null)
                    {
                        foreach (var i in muiCache.GetValueNames())
                        {
                            if (i.Contains("umamusume.exe"))
                                _gamePaths.Add(i[..i.IndexOf("umamusume.exe")]);
                        }
                    }
                }
                catch { }
                try
                {
                    var explorerFeatureUsage = Registry.CurrentUser.OpenSubKey("Software")?.OpenSubKey("Microsoft")?.OpenSubKey("Windows")?.OpenSubKey("CurrentVersion")?.OpenSubKey("Explorer")?.OpenSubKey("FeatureUsage")?.OpenSubKey("AppSwitched");
                    if (explorerFeatureUsage != null)
                    {
                        foreach (var i in explorerFeatureUsage.GetValueNames())
                        {
                            if (i.Contains("umamusume.exe"))
                                _gamePaths.Add(i[..i.IndexOf("umamusume.exe")]);
                        }
                    }
                }
                catch { }
                try
                {
                    var compatibilityAssistant = Registry.CurrentUser.OpenSubKey("Software")?.OpenSubKey("Microsoft")?.OpenSubKey("Windows NT")?.OpenSubKey("CurrentVersion")?.OpenSubKey("AppCompatFlags")?.OpenSubKey("Compatibility Assistant")?.OpenSubKey("Store");
                    if (compatibilityAssistant != null)
                    {
                        foreach (var i in compatibilityAssistant.GetValueNames())
                        {
                            if (i.Contains("umamusume.exe"))
                                _gamePaths.Add(i[..i.IndexOf("umamusume.exe")]);
                        }
                    }
                }
                catch { }
                try
                {
                    var gameConfigStore = Registry.CurrentUser.OpenSubKey("System")?.OpenSubKey("GameConfigStore")?.OpenSubKey("Children");
                    if (gameConfigStore != null)
                    {
                        foreach (var subkey in gameConfigStore.GetSubKeyNames())
                        {
                            var gameConfig = gameConfigStore.OpenSubKey(subkey);
                            if (gameConfig != null)
                            {
                                var value = gameConfig.GetValue("MatchedExeFullPath");
                                if (value is string path && path.Contains("umamusume.exe"))
                                {
                                    _gamePaths.Add(path[..path.IndexOf("umamusume.exe")]);
                                }
                            }
                        }
                    }
                }
                catch { }
                _gamePaths = _gamePaths.Distinct().ToList();
                foreach (var i in _gamePaths)
                {
                    var executableFilePath = Path.Combine(i, "umamusume.exe");
                    var modulePath = Path.Combine(i, "version.dll");
                    var compatiableModulePath = Path.Combine(i, "winhttp.dll");
                    if (File.Exists(executableFilePath))
                    {
                        if (!File.Exists(modulePath) && !File.Exists(compatiableModulePath))
                        {
                            gamePaths.Add(i);
                        }
                        else if (File.Exists(compatiableModulePath))
                        {
                            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(compatiableModulePath)));
                            if (!uraCoreHashs.Contains(hash))
                                gamePaths.Add(i);
                        }
                        else if (File.Exists(modulePath))
                        {
                            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modulePath)));
                            if (uraCoreHashs.First() != hash || !uraCoreHashs.Contains(hash))
                                gamePaths.Add(i);
                        }
                    }
                }
                return gamePaths;
            }
            set
            {
                gamePaths = value;
            }
        }
    }
}
