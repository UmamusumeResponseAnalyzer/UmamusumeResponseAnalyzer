using IniParser;
using IniParser.Model;
using MessagePack;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Localization;

namespace UmamusumeResponseAnalyzer
{
    internal static class Config
    {
        internal static string CONFIG_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", ".config");
        internal static Dictionary<string, IEnumerable<ConfigItem>> ConfigSet { get; set; } = new();
        internal static Dictionary<string, ConfigItem> Configuration { get; private set; } = new();
        internal static void Initialize()
        {
            Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer"));
            ConfigSet.Add(Resource.ConfigSet_Events, ConfigItem.From
            (
                Resource.ConfigSet_ParseSingleModeCheckEventResponse,
                Resource.ConfigSet_ParseTrainedCharaLoadResponse,
                Resource.ConfigSet_ParseFriendSearchResponse,
                Resource.ConfigSet_ParseTeamStadiumOpponentListResponse,
                Resource.ConfigSet_ParsePracticeRaceRaceStartResponse,
                Resource.ConfigSet_ParseRoomMatchRaceStartResponse,
                Resource.ConfigSet_ParseChampionsRaceStartResponse,
                Resource.ConfigSet_MaximiumGradeSkillRecommendation,
                Resource.ConfigSet_ShowCommandInfo
            ));
            ConfigSet.Add("更新", ConfigItem.From(
                Resource.ConfigSet_ForceUseGithubToUpdate
            ));
            ConfigSet.Add("加速", new ConfigItem[]
            {
                new(Resource.ConfigSet_EnableNetFilter,false),
                new("加速服务器地址",string.Empty,false),
                new("加速服务器端口",string.Empty,false),
                new("加速服务器用户名",string.Empty, false),
                new("加速服务器密码",string.Empty, false),
                new("加速服务器类型",string.Empty, false)
            });
            ConfigSet.Add("本地化", new ConfigItem[]
            {
                new(Resource.ConfigSet_LoadLocalizedData,true),
                new("本地化文件路径",string.Empty,false),
            });
            ConfigSet.Add("调试", ConfigItem.From(Resource.ConfigSet_SaveResponseForDebug));
            ConfigSet.Add("其他", ConfigItem.From(
                Resource.ConfigSet_DMMLaunch
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
                    AnsiConsole.MarkupLine($"[red]读取配置文件时发生错误,已重新生成,请再次更改设置[/]");
                }
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
                if (i.Key == Resource.ConfigSet_ForceUseGithubToUpdate ||
                    i.Key == Resource.ConfigSet_EnableNetFilter ||
                    i.Key == Resource.ConfigSet_DMMLaunch) //不默认开
                    Configuration.Add(i.Key, new(i.Key, false));
                else
                    Configuration.Add(i.Key, new(i.Key, true));
            }
            Save();
        }
        public static bool ContainsKey(string key) => Configuration.ContainsKey(key);
        public static T Get<T>(string key) => (T)Configuration[key].Value;
        public static bool Get(string key) => Configuration.ContainsKey(key) && Get<bool>(key);
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
            => arr.Select(x => new ConfigItem { Key = x });
        public override string ToString() => Value == null ? "NULL" : Value.ToString()!;
    }
}
