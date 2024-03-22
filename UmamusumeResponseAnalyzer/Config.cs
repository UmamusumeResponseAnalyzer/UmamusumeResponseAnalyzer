using IniParser;
using IniParser.Model;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using static UmamusumeResponseAnalyzer.Localization.Config;

namespace UmamusumeResponseAnalyzer
{
    internal static class Config
    {
        private readonly static List<object> _savedConfigForLanguageChange = [];
        internal static readonly IReadOnlyCollection<string> LanguageSectionKeys = ["Language", "言語", "语言"];
        internal static readonly IReadOnlyCollection<string> DisableByDefault = [I18N_ForceUseGithubToUpdate, I18N_EnableNetFilter, I18N_DMMLaunch, I18N_SaveResponseForDebug, I18N_WriteAIInfo, I18N_Language_English, I18N_Language_Japanese, I18N_Language_SimplifiedChinese];
        internal static string CONFIG_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", ".config");
        internal static Dictionary<string, IEnumerable<ConfigItem>> ConfigSet { get; set; } = [];
        internal static Dictionary<string, ConfigItem> Configuration { get; private set; } = [];
        internal static void Initialize()
        {
            Trace.WriteLine($"INIT WITH {Thread.CurrentThread.CurrentUICulture.Name}");
            Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer"));
            // 在初始化之前覆盖默认语言
            if (File.Exists(CONFIG_FILEPATH))
            {
                try
                {
                    var configuration = new FileIniDataParser().ReadFile(CONFIG_FILEPATH, Encoding.UTF8);
                    var language = configuration.Sections.First(x => LanguageSectionKeys.Contains(x.SectionName));
                    var lastLanguage = language.Keys.First(x => bool.Parse(x.Value));
                    UmamusumeResponseAnalyzer.ApplyCultureInfo(lastLanguage.KeyName);
                }
                catch (Exception)
                {
                }
            }
            ConfigSet.Add(I18N_Events, ConfigItem.From
            (
                I18N_ParseSingleModeCheckEventResponse,
                I18N_ParseTrainedCharaLoadResponse,
                I18N_ParseFriendSearchResponse,
                I18N_ParseTeamStadiumOpponentListResponse,
                I18N_ParsePracticeRaceRaceStartResponse,
                I18N_ParseRoomMatchRaceStartResponse,
                I18N_ParseChampionsRaceStartResponse,
                I18N_MaximiumGradeSkillRecommendation,
                I18N_ShowCommandInfo
            ));
            ConfigSet.Add(I18N_Update, ConfigItem.From(
                I18N_ForceUseGithubToUpdate
            ));
            ConfigSet.Add(I18N_NetworkProxy, new ConfigItem[]
            {
                new(I18N_EnableNetFilter,false),
                new(I18N_ProxyHost,string.Empty,false),
                new(I18N_ProxyPort,string.Empty,false),
                new(I18N_ProxyUsername,string.Empty, false),
                new(I18N_ProxyPassword,string.Empty, false),
                new(I18N_ProxyServerType,string.Empty, false)
            });
            ConfigSet.Add(I18N_Language, ConfigItem.From(
                I18N_Language_AutoDetect,
                I18N_Language_English,
                I18N_Language_Japanese,
                I18N_Language_SimplifiedChinese
                ));
            ConfigSet.Add(I18N_Localization, new ConfigItem[]
            {
                new(I18N_LoadLocalizedData,true),
                new(I18N_LocalizedDataPath,string.Empty,false),
            });
            ConfigSet.Add(I18N_Debug, ConfigItem.From(I18N_SaveResponseForDebug));
            ConfigSet.Add(I18N_Other, ConfigItem.From(
                I18N_DMMLaunch,
                I18N_DisableSelectIndex,
                I18N_WriteAIInfo
                ));
            if (File.Exists(CONFIG_FILEPATH))
            {
                try
                {
                    var configuration = new FileIniDataParser().ReadFile(CONFIG_FILEPATH, Encoding.UTF8);
                    foreach (var i in configuration.Sections.SelectMany(x => x.Keys))
                    {
                        if (bool.TryParse(i.Value, out var inBool))
                        {
                            Configuration.Add(i.KeyName, new(i.KeyName, inBool));
                        }
                        else
                        {
                            Configuration.Add(i.KeyName, new(i.KeyName, i.Value));
                        }
                    }
                    AddMissing();
                }
                catch (Exception)
                {
                    File.Delete(CONFIG_FILEPATH);
                    AddMissing();
                    AnsiConsole.MarkupLine(I18N_ConfigLoadError);
                }
            }
            else
            {
                AddMissing();
            }
        }
        internal static void SaveConfigForLanguageChange()
        {
            Trace.WriteLine(Configuration.Count);
            _savedConfigForLanguageChange.Clear();
            var cfg = Configuration.Values.ToArray();
            for (var i = 0; i < cfg.Length; i++)
            {
                Trace.WriteLine($"{cfg[i].Key} {cfg[i].Value}");
                _savedConfigForLanguageChange.Add(cfg[i].Value);
            }
        }
        internal static void LoadConfigForLanguageChange()
        {
            ConfigSet.Clear();
            Configuration.Clear();
            File.Delete(CONFIG_FILEPATH);
            Initialize();
            var cfg = Configuration.Keys.ToArray();
            for (var i = 0; i < _savedConfigForLanguageChange.Count; i++)
            {
                Configuration[cfg[i]].Value = _savedConfigForLanguageChange[i];
            }
        }
        public static void Save()
        {
            var ini = new IniData();
            foreach (var i in ConfigSet)
            {
                var section = new SectionData(i.Key);
                foreach (var j in ConfigSet[i.Key])
                {
                    if (!Configuration.ContainsKey(j.Key)) continue;
                    section.Keys[j.Key] = Configuration[j.Key].ToString();
                }
                ini.Sections.Add(section);
            }
            var configSets = ini.Sections.SelectMany(x => x.Keys).Select(x => x.KeyName);
            new FileIniDataParser().WriteFile(CONFIG_FILEPATH, ini, Encoding.UTF8);
        }
        private static void AddMissing()
        {
            foreach (var i in ConfigSet.SelectMany(x => x.Value))
            {
                if (Configuration.ContainsKey(i.Key)) continue;
                if (DisableByDefault.Contains(i.Key)) //不默认开
                {
                    Configuration.Add(i.Key, new(i.Key, false));
                }
                else
                {
                    switch (Type.GetTypeCode(i.Value.GetType()))
                    {
                        case TypeCode.Boolean:
                            Configuration.Add(i.Key, new(i.Key, true));
                            break;
                        case TypeCode.String:
                            Configuration.Add(i.Key, new(i.Key, string.Empty));
                            break;
                    }
                }
            }
            Save();
        }
        public static bool ContainsKey(string key)
        {
            if (Configuration.TryGetValue(key, out var value))
            {
                return !string.IsNullOrEmpty(value.Value.ToString());
            }
            else
            {
                return false;
            }
        }
        public static bool Get(string key) => Get<bool>(key);
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T">bool||string</typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        public static T? Get<T>(string key)
        {
            if (Configuration.TryGetValue(key, out var value))
            {
                if (value.Value is T t)
                {
                    return t;
                }
                else
                {
                    AnsiConsole.MarkupLine(I18N_ConfigTypeCastError, key, typeof(T), value.GetType());
                }
            }
            return default;
        }
        public static bool TryGetValue(string key, out ConfigItem? value)
        {
            if (Configuration.TryGetValue(key, out value))
            {
                if (!string.IsNullOrEmpty(value.Value.ToString()))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                value = default;
                return false;
            }
        }
        public static void Set(string key, object value)
        {
            if (!Configuration.ContainsKey(key)) Configuration.Add(key, new(key, false, false));
            Configuration[key].Value = value;
        }
    }
    public class ConfigItem
    {
        public string Key { get; set; }
        public object Value { get; set; }
        public bool Visiable { get; set; }
        private ConfigItem(bool visiable = true) { Visiable = visiable; }
        public ConfigItem(string key, object value, bool visiable = true)
        {
            Key = key;
            Value = value;
            Visiable = visiable;
        }

        public static IEnumerable<ConfigItem> From(params string[] arr)
            => arr.Select(x => new ConfigItem { Key = x, Value = true });
        public override string ToString() => Value == null ? "NULL" : Value.ToString()!;
    }
}
