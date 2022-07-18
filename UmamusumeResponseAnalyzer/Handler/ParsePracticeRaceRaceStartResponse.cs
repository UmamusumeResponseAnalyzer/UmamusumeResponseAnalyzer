using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParsePracticeRaceRaceStartResponse(byte[] buffer) //练习赛
        {
            var @event = TryDeserialize<Gallop.PracticeRaceRaceStartResponse>(buffer);
            if (@event != default)
            {
                var data = @event.data;

                Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "races"));
                var lines = new List<string>
                {
                    $"Race Scenario:",
                    data.race_result_info.race_scenario,
                    string.Empty,
                    $"Race Horse Data Array",
                    JsonConvert.SerializeObject(data.race_result_info.race_horse_data_array),
                    string.Empty,
                    $"Trained Characters:"
                };
                foreach (var i in data.trained_chara_array)
                {
                    lines.Add(JsonConvert.SerializeObject(i, Formatting.None));
                    lines.Add(string.Empty);
                }
                File.WriteAllLines(@$"{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "races")}/{DateTime.Now:yy-MM-dd HH-mm-ss-fff} PracticeRace.txt", lines);
            }
        }
    }
}
