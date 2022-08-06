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
        internal static Dictionary<string, string[]> ConfigSet { get; set; } = new();
        internal static Dictionary<string, object> Configuration { get; private set; } = new();
        internal static void Initialize()
        {
            Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer"));
            ConfigSet.Add(Resource.ConfigSet_Events, new[]
            {
                Resource.ConfigSet_ParseSingleModeCheckEventResponse,
                Resource.ConfigSet_ParseTrainedCharaLoadResponse,
                Resource.ConfigSet_ParseFriendSearchResponse,
                Resource.ConfigSet_ParseTeamStadiumOpponentListResponse,
                Resource.ConfigSet_ParsePracticeRaceRaceStartResponse,
                Resource.ConfigSet_ParseRoomMatchRaceStartResponse,
                Resource.ConfigSet_ParseChampionsRaceStartResponse,
                Resource.ConfigSet_MaximiumGradeSkillRecommendation,
                Resource.ConfigSet_ShowCommandInfo
            });
            ConfigSet.Add("更新", new[]
            {
                Resource.ConfigSet_AutoUpdate,
                Resource.ConfigSet_ForceUseGithubToUpdate
            });
            ConfigSet.Add("调试", new[]
            {
                Resource.ConfigSet_SaveResponseForDebug
            });
            ConfigSet.Add("其他", new[]
            {
                Resource.ConfigSet_EnableNetFilter,
                Resource.ConfigSet_DMMLaunch
            });
            if (File.Exists(CONFIG_FILEPATH))
            {
                try
                {
                    var configuration = new FileIniDataParser().ReadFile(CONFIG_FILEPATH, Encoding.UTF8);
                    foreach (var i in configuration.Sections.SelectMany(x => x.Keys))
                    {
                        if (bool.TryParse(i.Value, out var inBool))
                        {
                            Configuration.Add(i.KeyName, inBool);
                        }
                        else
                        {
                            Configuration.Add(i.KeyName, i.Value);
                        }
                    }
                }
                catch (Exception)
                {
                    File.Delete(CONFIG_FILEPATH);
                    Generate();
                    AnsiConsole.MarkupLine($"[red]读取配置文件时发生错误,已重新生成,请再次更改设置[/]");
                }
            }
            else
            {
                Generate();
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
                    section.Keys[j] = Configuration[j].ToString();
                }
                ini.Sections.Add(section);
            }
            new FileIniDataParser().WriteFile(CONFIG_FILEPATH, ini, Encoding.UTF8);
        }
        private static void Generate()
        {
            foreach (var i in ConfigSet.SelectMany(x => x.Value))
            {
                if (i == Resource.ConfigSet_ForceUseGithubToUpdate ||
                    i == Resource.ConfigSet_EnableNetFilter ||
                    i == Resource.ConfigSet_DMMLaunch) //不默认开
                    Configuration.Add(i, false);
                else
                    Configuration.Add(i, true);
            }
            Save();
        }
        public static bool ContainsKey(string key) => Configuration.ContainsKey(key);
        public static T Get<T>(string key) => (T)Configuration[key];
        public static bool Get(string key) => Get<bool>(key);
        public static void Set(string key, object value)
        {
            Configuration[key] = value;
        }
    }
}
