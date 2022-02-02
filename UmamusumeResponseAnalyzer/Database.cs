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
        const string EVENT_NAME_FILEPATH = @"./events.json";
        const string SUCCESS_EVENT_FILEPATH = @"./successevent.json";
        public static Dictionary<long, Story> Events = new();
        public static Dictionary<int, (int Choice, string Effect)> SuccessEvent = new();
        public static void Initialize()
        {
            if (File.Exists(EVENT_NAME_FILEPATH))
            {
                Events = JArray.Parse(File.ReadAllText(EVENT_NAME_FILEPATH)).ToObject<List<Story>>().ToDictionary(x => x.Id, x => x);
            }
            if (File.Exists(SUCCESS_EVENT_FILEPATH))
                SuccessEvent = JObject.Parse(File.ReadAllText(SUCCESS_EVENT_FILEPATH)).ToObject<Dictionary<int, (int Choice, string Effect)>>();
        }
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
    public string Effect { get; set; }
    public Choice(string opt, string eff)
    {
        Option = opt;
        Effect = eff;
    }
}