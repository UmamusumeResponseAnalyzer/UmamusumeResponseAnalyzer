using Gallop;
using MathNet.Numerics.Distributions;
using Newtonsoft.Json;
using Spectre.Console;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using static UmamusumeResponseAnalyzer.Game.TurnInfo.TurnInfoUAF;
using static UmamusumeResponseAnalyzer.Localization.CommandInfo.UAF;
using static UmamusumeResponseAnalyzer.Localization.Game;
using System.Text;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static class Debug {
        public static void Dump(object o, string tag="")
        {
            int turn = GameStats.currentTurn;
            string pathname = $"Logs/Turn{GameStats.currentTurn}";
            if (tag.Length > 0)
                pathname += $"_{tag}";
            string suffix = "";
            int n = 0;
            while (File.Exists(pathname + suffix + ".json"))
                suffix = $"_{++n}";
            
            File.WriteAllText(pathname + suffix + ".json", JsonConvert.SerializeObject(o), Encoding.UTF8);
        }

        public static void AppendLog(object o, string tag)
        {
            string pathname = $"Logs/{tag}.json";
            List<object>? log = new List<object>();
            if (File.Exists(pathname))
                log = JsonConvert.DeserializeObject<List<object>>(File.ReadAllText(pathname, Encoding.UTF8));
            if (log != null) {
                log.Add(o);
                File.WriteAllText(pathname, JsonConvert.SerializeObject(log), Encoding.UTF8);
            }
            
        }
    }
}
