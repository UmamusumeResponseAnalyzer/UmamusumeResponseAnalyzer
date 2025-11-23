using Newtonsoft.Json;
using Spectre.Console;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Plugin;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using i18n = UmamusumeResponseAnalyzer.Localization.Config;

namespace UmamusumeResponseAnalyzer
{
    public static class Config
    {
        internal static string CONFIG_FILEPATH = "config.yaml";
        private static YamlConfig Current { get; set; }
        private readonly static ISerializer _serializer = new SerializerBuilder().WithQuotingNecessaryStrings().WithNamingConvention(HyphenatedNamingConvention.Instance).Build();
        private readonly static IDeserializer _deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().WithNamingConvention(HyphenatedNamingConvention.Instance).Build();
        public static CoreConfig Core => Current.Core;
        public static RepositoryConfig Repository => Current.Repository;
        public static PluginConfig Plugin => Current.Plugin;
        public static UpdaterConfig Updater => Current.Updater;
        public static NetFilterConfig NetFilter => Current.NetFilter;
        public static DMMConfig DMM => Current.DMM;
        public static LanguageConfig Language => Current.Language;
        public static MiscConfig Misc => Current.Misc;

        internal static void Initialize()
        {
            if (File.Exists(CONFIG_FILEPATH))
            {
                Current = _deserializer.Deserialize<YamlConfig>(File.ReadAllText(CONFIG_FILEPATH));
                foreach (var property in Current.GetType().GetProperties())
                {
                    var value = property.GetValue(Current);
                    if (value == default)
                    {
                        property.SetValue(Current, property.PropertyType.GetConstructor(Type.EmptyTypes)!.Invoke([]));
                    }
                }
                UmamusumeResponseAnalyzer.ApplyCultureInfo(Language.Selected);
            }
            else
            {
                Current = new()
                {
                    Core = new(),
                    Repository = new(),
                    Plugin = new(),
                    Updater = new(),
                    NetFilter = new(),
                    DMM = new(),
                    Language = new(),
                    Misc = new()
                };
                Save();
            }
        }

        public static void Save() =>
            File.WriteAllText(CONFIG_FILEPATH, _serializer.Serialize(Current));

        public static void Prompt()
        {
            while (true)
            {
                var prompt = string.Empty;
                var tabs = typeof(YamlConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name);
                var translatedTabs = tabs.ToDictionary(x => i18n.ResourceManager.GetString($"Tabs_{x}_Title", i18n.Culture)!, x => x);
                var selection = new SelectionPrompt<string>()
                    .Title(i18n.Settings_Title)
                    .WrapAround(true)
                    .AddChoices(translatedTabs.Keys)
                    .AddChoices(i18n.Return)
                    .PageSize(30);
                prompt = AnsiConsole.Prompt(selection);
                if (prompt == i18n.Return) break;
                var config = typeof(Config).GetProperty(translatedTabs[prompt])?.GetValue(null);
                config?.GetType()?.GetMethod("Prompt")?.Invoke(config, null);
            }
        }
    }
    public class YamlConfig
    {
        public CoreConfig Core { get; set; }
        public RepositoryConfig Repository { get; set; }
        public PluginConfig Plugin { get; set; }
        public UpdaterConfig Updater { get; set; }
        public NetFilterConfig NetFilter { get; set; }
        public DMMConfig DMM { get; set; }
        public LanguageConfig Language { get; set; }
        public MiscConfig Misc { get; set; }
    }
    #region class
    public class CoreConfig
    {
        public string ListenAddress { get; set; } = "127.0.0.1";
        public int ListenPort { get; set; } = 4693;
        public bool RequestAdditionalHeader { get; set; } = false;
        public bool ShowFirstRunPrompt { get; set; } = true;
        public void Prompt()
        {
            var _properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var translated = _properties.Select(x => x.Name).ToDictionary(x => x, x => i18n.ResourceManager.GetString($"Tabs_Core_{x}", i18n.Culture)!);
            var selected = string.Empty;
            do
            {
                var selectionPrompt = new SelectionPrompt<string>()
                    .Title(i18n.Tabs_Core_Title)
                    .WrapAround(true)
                    .AddChoices(_properties.Select(x => x.AppendValue(this, translated)))
                    .AddChoices(i18n.Return);
                selected = AnsiConsole.Prompt(selectionPrompt).Split(':')[0];
                if (selected == i18n.Tabs_Core_ListenAddress)
                {
                    var address = string.Empty;
                    do
                    {
                        address = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_Core_ListenAddressPrompt));
                        if (IPAddress.TryParse(address, out _))
                        {
                            ListenAddress = address;
                            AnsiConsole.Clear();
                            Config.Save();
                            break;
                        }
                    } while (true);
                }
                else if (selected == i18n.Tabs_Core_ListenPort)
                {
                    var port = string.Empty;
                    do
                    {
                        port = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_Core_ListenPortPrompt));
                        if (int.TryParse(port, out var portInt))
                        {
                            ListenPort = portInt;
                            AnsiConsole.Clear();
                            Config.Save();
                            break;
                        }
                    } while (true);
                }
                else if (selected == i18n.Tabs_Core_RequestAdditionalHeader)
                {
                    RequestAdditionalHeader = !RequestAdditionalHeader;
                }
                else if (selected == nameof(ShowFirstRunPrompt))
                {
                    ShowFirstRunPrompt = !ShowFirstRunPrompt;

                }
                Config.Save();
            } while (selected != i18n.Return);
        }
    }
    public class RepositoryConfig
    {
        public List<string> Targets { get; set; } = [];
        public Dictionary<string, string> AdditionalPluginRepositories { get; set; } = [];

        public void Prompt()
        {
            var _properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.Name != "AdditionalPluginRepositories");
            var translated = _properties.Select(x => x.Name).ToDictionary(x => x, x => i18n.ResourceManager.GetString($"Tabs_Repository_{x}", i18n.Culture)!);
            var selected = string.Empty;
            do
            {
                var selectionPrompt = new SelectionPrompt<string>()
                    .Title(i18n.Tabs_Repository_Title)
                    .WrapAround(true)
                    .AddChoices(_properties.Select(x => x.AppendValue(this, translated)))
                    .AddChoices(AdditionalPluginRepositories.Select(x => $"{x.Key}: {x.Value}"))
                    .AddChoices([i18n.Add, i18n.Return]);
                selected = AnsiConsole.Prompt(selectionPrompt).Split(':')[0];

                if (selected == i18n.Tabs_Repository_Targets)
                {
                    var targetPrompt = new TextPrompt<string>(i18n.Tabs_Repository_TargetsPrompt)
                        .AllowEmpty();
                    var targetsInput = AnsiConsole.Prompt(targetPrompt);
                    Targets = string.IsNullOrEmpty(targetsInput) ? [] : [.. targetsInput.Replace('，', ',').Split(',')];
                    AnsiConsole.Clear();
                }
                else if (selected == i18n.Add)
                {
                    var urlPrompt = new TextPrompt<string>(i18n.Tabs_Repository_AdditionalPluginRepositoriesPrompt).AllowEmpty();
                    do
                    {
                        var url = AnsiConsole.Prompt(urlPrompt);
                        if (string.IsNullOrEmpty(url)) break;
                        if (Uri.TryCreate(url, UriKind.Absolute, out var _))
                        {
                            var namePrompt = new TextPrompt<string>(i18n.Tabs_Repository_AdditionalPluginRepositoriesNamePrompt)
                                .AllowEmpty();
                            var name = AnsiConsole.Prompt(namePrompt);
                            if (string.IsNullOrEmpty(name))
                            {
                                name = url.GetHashCode().ToString();
                            }
                            AdditionalPluginRepositories.Add(name, url);
                            break;
                        }
                    } while (true);
                    AnsiConsole.Clear();
                }
                else
                {
                    AdditionalPluginRepositories.Remove(selected);
                }

                Config.Save();
            } while (selected != i18n.Return);
        }
    }
    public class PluginConfig
    {
        // 插件名 -> (属性名 -> 值)
        public Dictionary<string, Dictionary<string, object>> PluginSettings { get; set; } = [];

        public void Prompt()
        {
            UmamusumeResponseAnalyzer._plugin_initialize_task.Wait();
            var selected = string.Empty;
            var plugins = PluginManager.LoadedPlugins.ToDictionary(x => x.GetType().GetProperty("Name")?.GetCustomAttribute<PluginDescriptionAttribute>()?.Description ?? x.Name, x => x);
            do
            {
                var selectionPrompt = new SelectionPrompt<string>()
                    .Title(i18n.Tabs_Plugin_Title)
                    .WrapAround(true)
                    .AddChoices(plugins.Keys)
                    .AddChoices(i18n.Return)
                    .PageSize(30);
                selected = AnsiConsole.Prompt(selectionPrompt);
                if (selected != i18n.Return)
                {
                    var plugin = plugins[selected];
                    plugin.ConfigPrompt();
                    Config.Save();
                }
            } while (selected != i18n.Return);
        }
    }
    public class UpdaterConfig
    {
        public bool IsGithubBlocked { get; set; } = RegionInfo.CurrentRegion.Name == "CN" || CultureInfo.CurrentUICulture.Name == "zh-CN";
        public bool TrainerIsMale { get; set; } = true;
        public string DatabaseLanguage { get; set; }
        public string CustomDatabaseRepository { get; set; }
        public bool ForceUseGithubToUpdate { get; set; }
        public void Prompt()
        {
            var _properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.Name != "IsGithubBlocked");
            var translated = _properties.Select(x => x.Name).ToDictionary(x => x, x => i18n.ResourceManager.GetString($"Tabs_Updater_{x}", i18n.Culture)!);

            var selected = string.Empty;
            do
            {
                var l3Prompt = new SelectionPrompt<string>()
                    .Title(i18n.Tabs_Updater_Title)
                    .WrapAround(true)
                    .AddChoices(_properties.Select(x => x.AppendValue(this, translated)))
                    .AddChoices(i18n.Return);
                selected = AnsiConsole.Prompt(l3Prompt).Split(':')[0];
                if (selected == nameof(TrainerIsMale))
                {
                    TrainerIsMale = !TrainerIsMale;
                }
                else if (selected == nameof(DatabaseLanguage))
                {
                    var dbLangPrompt = new SelectionPrompt<string>()
                        .Title(nameof(DatabaseLanguage))
                        .WrapAround(true)
                        .AddChoices(["ja-JP", "zh-TW", "zh-CN"]);
                    var dbLang = AnsiConsole.Prompt(dbLangPrompt);
                    DatabaseLanguage = dbLang;
                    AnsiConsole.Clear();
                }
                else if (selected == nameof(CustomDatabaseRepository))
                {
                    var urlPrompt = new TextPrompt<string>(i18n.Tabs_Repository_AdditionalPluginRepositoriesPrompt).AllowEmpty();
                    do
                    {
                        var url = AnsiConsole.Prompt(urlPrompt);
                        if (string.IsNullOrEmpty(url))
                        {
                            CustomDatabaseRepository = string.Empty;
                            break;
                        }
                        if (Uri.TryCreate(url, UriKind.Absolute, out var _))
                        {
                            CustomDatabaseRepository = url;
                            break;
                        }
                    } while (true);
                    AnsiConsole.Clear();
                }
                else if (selected == i18n.Tabs_Updater_ForceUseGithubToUpdate)
                {
                    ForceUseGithubToUpdate = !ForceUseGithubToUpdate;
                }
            } while (selected != i18n.Return);
            Config.Save();
        }
    }
    public class NetFilterConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string ServerType { get; set; }
        public bool Enable { get; set; }
        public void Prompt()
        {
            if (!Enable)
            {
                var enablePrompt = new ConfirmationPrompt(i18n.Tabs_NetFilter_Caution)
                {
                    DefaultValue = false
                };
                if (AnsiConsole.Prompt(enablePrompt))
                {
                    Enable = true;
                    Config.Save();
                    Prompt();
                }
                else
                {
                    AnsiConsole.Clear();
                }
                return;
            }
            var _properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var translated = _properties.Select(x => x.Name).ToDictionary(x => x, x => i18n.ResourceManager.GetString($"Tabs_NetFilter_{x}", i18n.Culture)!);
            var selected = string.Empty;
            do
            {
                var selectionPrompt = new SelectionPrompt<string>()
                    .Title(i18n.Tabs_NetFilter_Title)
                    .WrapAround(true)
                    .AddChoices(_properties.Select(x => $"{translated[x.Name]}: {x.GetValue(this)}"))
                    .AddChoices(i18n.Return);
                selected = AnsiConsole.Prompt(selectionPrompt).Split(':')[0];
                if (selected == i18n.Tabs_NetFilter_Enable)
                {
                    Enable = false;
                    selected = i18n.Return;
                }
                else if (selected == i18n.Tabs_NetFilter_Host)
                {
                    Host = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_NetFilter_InputHost));
                }
                else if (selected == i18n.Tabs_NetFilter_Port)
                {
                    do
                    {
                        var portText = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_NetFilter_InputPort));
                        if (int.TryParse(portText, out var port))
                        {
                            Port = port;
                            break;
                        }
                        else
                        {
                            AnsiConsole.WriteLine(i18n.Tabs_NetFilter_InputPort_FormatError);
                        }
                    } while (true);
                }
                else if (selected == i18n.Tabs_NetFilter_Username)
                {
                    Username = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_NetFilter_InputUsername).AllowEmpty());
                }
                else if (selected == i18n.Tabs_NetFilter_Password)
                {
                    Password = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_NetFilter_InputPassword).AllowEmpty());
                }
                else if (selected == i18n.Tabs_NetFilter_ServerType)
                {
                    ServerType = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_NetFilter_SelectType).AddChoices(["socks5", "http", "socks4a"]).DefaultValue("socks5"));
                }
                Config.Save();
            } while (selected != i18n.Return);
        }
    }
    public class DMMConfig
    {
        public DMMLauncherInfomation LauncherInfomation { get; set; } = new();
        public DMMMachineInformation MachineInformation { get; set; } = new();
        public List<DMMAccountInformation> Accounts { get; set; } = [];
        public bool Enable { get; set; }

        public void Prompt()
        {
            if (!Enable)
            {
                var enablePrompt = new ConfirmationPrompt(i18n.Tabs_DMM_Caution)
                {
                    DefaultValue = false
                };
                if (AnsiConsole.Prompt(enablePrompt))
                {
                    Enable = true;
                    Config.Save();
                    Prompt();
                }
                else
                {
                    AnsiConsole.Clear();
                }
                return;
            }
            var selected = string.Empty;
            do
            {
                var selectionPrompt = new SelectionPrompt<string>()
                    .Title(i18n.Tabs_DMM_Title)
                    .WrapAround(true)
                    .AddChoices([i18n.Tabs_DMM_ViewLauncherInformation, i18n.Tabs_DMM_EditMachineInformation, i18n.Tabs_DMM_EditAccounts, i18n.Tabs_DMM_Enable])
                    .AddChoices(i18n.Return);
                selected = AnsiConsole.Prompt(selectionPrompt).Split(':')[0];
                if (selected == i18n.Tabs_DMM_Enable)
                {
                    Enable = false;
                    selected = i18n.Return;
                }
                else if (selected == i18n.Tabs_DMM_ViewLauncherInformation)
                {
                    LauncherInfomation.Prompt();
                }
                else if (selected == i18n.Tabs_DMM_EditMachineInformation)
                {
                    MachineInformation.Prompt();
                }
                else if (selected == i18n.Tabs_DMM_EditAccounts)
                {
                    var eaSelected = string.Empty;
                    do
                    {
                        var eaPrompt = new SelectionPrompt<string>()
                            .Title(i18n.Tabs_DMM_EditAccounts)
                            .WrapAround(true)
                            .AddChoices(Accounts.Select(x => x.Name))
                            .AddChoices([i18n.Tabs_DMM_EditAccounts_Add, i18n.Return]);
                        eaSelected = AnsiConsole.Prompt(eaPrompt);

                        if (eaSelected == i18n.Tabs_DMM_EditAccounts_Add)
                        {
                            var account = new DMMAccountInformation();
                            Accounts.Add(account);
                            account.EditPrompt();
                        }
                        else if (eaSelected == i18n.Return)
                        {
                        }
                        else
                        {
                            var account = Accounts.First(x => x.Name == eaSelected);
                            account.EditPrompt();
                        }
                    } while (eaSelected != i18n.Return);
                }
                Config.Save();
            } while (selected != i18n.Return);
        }

        public class DMMLauncherInfomation
        {
            public string AcceptEncoding { get; set; } = "gzip, deflate, br, zstd";
            public string AcceptLanguage { get; set; } = "zh-CN";
            public string UserAgent { get; set; } = "DMMGamePlayer5-Win/5.4.2 Electron/34.3.0";
            public string ClientApp { get; set; } = "DMMGamePlayer5";
            public string ClientVersion { get; set; } = "5.4.2";
            public string SecFetchDest { get; set; } = "empty";
            public string SecFetchMode { get; set; } = "no-cors";
            public string SecFetchSite { get; set; } = "none";

            public void Prompt()
            {
                var liSelectionPrompt = new SelectionPrompt<string>()
                    .Title(i18n.Tabs_DMM_ViewLauncherInformation_Caution)
                    .WrapAround(true)
                    .AddChoices(GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => $"{x.Name}: {x.GetValue(this)}"));
                AnsiConsole.Prompt(liSelectionPrompt);
                Config.Save();
            }
        }
        public class DMMMachineInformation
        {
            public string mac_address { get; set; }
            public string hdd_serial { get; set; }
            public string motherboard { get; set; }
            public string user_os { get; set; }
            public string umamusume_file_path { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Umamusume", "umamusume.exe");

            public void Prompt()
            {
                var miProperties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var miSelected = string.Empty;
                do
                {
                    var miSelectionPrompt = new SelectionPrompt<string>()
                        .Title(i18n.Tabs_DMM_EditMachineInformation)
                        .WrapAround(true)
                        .AddChoices(miProperties.Select(x => $"{x.Name}: {x.GetValue(this)}"))
                        .AddChoices(i18n.Return);
                    miSelected = AnsiConsole.Prompt(miSelectionPrompt);

                    switch (miSelected.Split(':')[0])
                    {
                        case "mac_address":
                            {
                                mac_address = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_DMM_EditMachineInformation_InputMA));
                                break;
                            }
                        case "hdd_serial":
                            {
                                hdd_serial = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_DMM_EditMachineInformation_InputHS));
                                break;
                            }
                        case "motherboard":
                            {
                                motherboard = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_DMM_EditMachineInformation_InputMB));
                                break;
                            }
                        case "user_os":
                            {
                                user_os = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_DMM_EditMachineInformation_InputOS));
                                break;
                            }
                        case "umamusume_file_path":
                            {
                                umamusume_file_path = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_DMM_EditMachineInformation_InputUmamusumePath));
                                break;
                            }
                    }
                    AnsiConsole.Clear();
                    Config.Save();
                } while (miSelected != i18n.Return);
            }
        }
        public class DMMAccountInformation
        {
            public string Name { get; set; }
            public string access_token { get; set; }
            public string split_umamusume_file_path { get; set; }
            public string savedata_file_path { get; set; }

            public void EditPrompt()
            {
                var aiSelected = string.Empty;
                do
                {
                    var aiPrompt = new SelectionPrompt<string>()
                    .WrapAround(true)
                    .AddChoices([$"{i18n.Tabs_DMM_EditAccounts_Name}: {Name}", "access_token", $"{i18n.Tabs_DMM_EditAccounts_SplitPath}: {split_umamusume_file_path}", $"{i18n.Tabs_DMM_EditAccounts_SavePath}: {savedata_file_path}"])
                    .AddChoices(i18n.Tabs_DMM_EditAccounts_Delete, i18n.Return);
                    aiSelected = AnsiConsole.Prompt(aiPrompt).Split(':')[0];
                    if (aiSelected == i18n.Tabs_DMM_EditAccounts_Name)
                    {
                        while (true)
                        {
                            var name = AnsiConsole.Prompt(new TextPrompt<string>(i18n.Tabs_DMM_EditAccounts_InputName));
                            if (Config.DMM.Accounts.Any(x => x.Name == name))
                            {
                                AnsiConsole.WriteLine(i18n.Tabs_DMM_EditAccounts_InputName_Duplicate);
                            }
                            else
                            {
                                Name = name;
                                break;
                            }
                        }
                    }
                    else if (aiSelected == "access_token")
                    {
                        var readyToOpenWeb = new ConfirmationPrompt("即将打开网页，请在浏览器内登录DMM账号但不要关闭网页");
                        if (AnsiConsole.Prompt(readyToOpenWeb))
                        {
                            var loginUrl = DMM.LoginUrl().Result;
                            Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });
                            var code = AnsiConsole.Ask<string>("请输入地址栏中，code=后面的内容");
                            var accessToken = DMM.IssueAccessToken(code).Result;
                            access_token = accessToken;
                        }
                    }
                    else if (aiSelected == "split_umamusume_file_path")
                    {
                        split_umamusume_file_path = AnsiConsole.Prompt(new TextPrompt<string>("Input split_umamusume_file_path"));
                    }
                    else if (aiSelected == "savedata_file_path")
                    {
                        savedata_file_path = AnsiConsole.Prompt(new TextPrompt<string>("Input savedata_file_path"));
                    }
                    else if (aiSelected == i18n.Tabs_DMM_EditAccounts_Delete)
                    {
                        aiSelected = i18n.Return;
                        Config.DMM.Accounts.Remove(this);
                    }
                    AnsiConsole.Clear();
                    Config.Save();
                } while (aiSelected != i18n.Return);
            }
        }
    }
    public class LanguageConfig
    {
        public Language Selected { get; private set; } = Language.AutoDetect;

        public void Prompt()
        {
            var languageProperties = Enum.GetNames(typeof(Language));
            var translated = languageProperties.ToDictionary(x => i18n.ResourceManager.GetString($"Tabs_Language_{x}", i18n.Culture)!, x => x);
            var languagePrompt = new SelectionPrompt<string>()
                .Title(i18n.Tabs_Language_Title)
                .AddChoices(translated.Keys);

            var selected = AnsiConsole.Prompt(languagePrompt);
            if (translated.TryGetValue(selected, out var languageName) && Enum.TryParse<Language>(languageName, out var langEnum))
            {
                Selected = langEnum;
            }
            Config.Save();
            UmamusumeResponseAnalyzer.Restart();
        }
        public static string GetCulture()
        {
            return Config.Language.Selected switch
            {
                Language.SimplifiedChinese => "zh-CN",
                Language.Japanese => "ja-JP",
                Language.English => "en-US",
                _ => Thread.CurrentThread.CurrentCulture.Name,
            };
        }
        public enum Language
        {
            AutoDetect,
            SimplifiedChinese,
            Japanese,
            English
        }
    }
    public class MiscConfig
    {
        public bool SaveResponseForDebug { get; set; }
        public void Prompt()
        {
            var _properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var translated = _properties.Select(x => x.Name).ToDictionary(x => x, x => i18n.ResourceManager.GetString($"Tabs_Debug_{x}", i18n.Culture)!);
            var l3Prompt = new MultiSelectionPrompt<string>()
                .Title(i18n.Tabs_Debug_Title)
                .WrapAround(true)
                .Required(false)
                .AddChoices(translated.Values);
            foreach (var i in _properties)
            {
                if ((bool)i.GetValue(this)!)
                {
                    l3Prompt.Select(translated[i.Name]);
                }
            }
            var l3 = AnsiConsole.Prompt(l3Prompt);
            foreach (var i in _properties)
            {
                i.SetValue(this, l3.Contains(translated[i.Name]));
            }
            Config.Save();
        }
    }
    #endregion
}
