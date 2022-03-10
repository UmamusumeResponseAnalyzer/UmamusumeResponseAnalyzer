using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer
{
    public static class Database
    {
        internal static string EVENT_NAME_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "events.json");
        internal static string SUCCESS_EVENT_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "successevents.json");
        internal static string RACE_CODES_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "races.json");
        internal static string ID_TO_NAME_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "id.json");
        public static Dictionary<long, Story> Events { get; set; } = new();
        public static Dictionary<string, SuccessStory> SuccessEvent { get; set; } = new();
        public static Dictionary<string, string> Races { get; set; } = new();
        public static Dictionary<long, string> IdToName { get; set; } = new();
        public static void Initialize()
        {
            Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer"));
            if (File.Exists(EVENT_NAME_FILEPATH))
                Events = JArray.Parse(File.ReadAllText(EVENT_NAME_FILEPATH)).ToObject<List<Story>>().ToDictionary(x => x.Id, x => x);
            if (File.Exists(SUCCESS_EVENT_FILEPATH))
                SuccessEvent = JArray.Parse(File.ReadAllText(SUCCESS_EVENT_FILEPATH)).ToObject<List<SuccessStory>>().ToDictionary(x => x.Name, x => x);
            if (File.Exists(RACE_CODES_FILEPATH))
                Races = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(RACE_CODES_FILEPATH));
            if (File.Exists(ID_TO_NAME_FILEPATH))
                IdToName = JsonConvert.DeserializeObject<Dictionary<long, string>>(File.ReadAllText(ID_TO_NAME_FILEPATH));
        }
    }
    public class Story
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string TriggerName { get; set; }
        public List<Choice> Choices { get; set; }

    }
    public class Choice
    {
        public string Option { get; set; }
        public string SuccessEffect { get; set; }
        public string FailedEffect { get; set; }
    }
    public class SuccessStory
    {
        public string Name { get; set; }
        public List<SuccessChoice> Choices { get; set; } = new();
    }
    public class SuccessChoice
    {
        public int ChoiceIndex { get; set; }
        public int SelectIndex { get; set; }
        public SuccessChoiceEffectDictionary Effects { get; set; } //ScenarioId-Effect
    }
    public class SuccessChoiceEffectDictionary : Dictionary<int, string>
    {
        public new string this[int key]
        {
            get => ContainsKey(key) ? base[key] : Values.First(); //如果有对应剧本的效果则返回，否则返回第一条
            set => base[key] = value;
        }
    }
}