using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer
{
    internal static class Config
    {
        internal static Dictionary<string, string[]> ConfigSet { get; set; } = new();
        internal static Dictionary<string, bool> Configuration { get; set; } = new();
        static Config()
        {
            ConfigSet.Add("Events", new[]
            {
                "ParseSingleModeCheckEventResponse",
                "ParseTrainedCharaLoadResponse",
                "ParseFriendSearchResponse",
                "ParseTeamStadiumOpponentListResponse"
            });
            ConfigSet.Add("Test", Array.Empty<string>());
            if (File.Exists(@".config"))
            {
                Configuration = MessagePackSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllBytes(@".config"));
                foreach (var i in ConfigSet)
                {
                    if (i.Value == Array.Empty<string>())
                    {
                        if (!Configuration.ContainsKey(i.Key))
                            Configuration.Add(i.Key, false); //对于新添加的功能 默认不开启
                    }
                    else
                    {
                        foreach (var j in i.Value)
                        {
                            Configuration.Add(j, false);
                        }
                    }
                }
            }
            else
            {
                foreach (var i in ConfigSet)
                {
                    if (i.Value == Array.Empty<string>())
                    {
                        Configuration.Add(i.Key, true);
                    }
                    else
                    {
                        foreach (var j in i.Value)
                        {
                            Configuration.Add(j, true);
                        }
                    }
                }
                File.WriteAllBytes(@".config", MessagePackSerializer.Serialize(Configuration));
            }
        }
    }
}
