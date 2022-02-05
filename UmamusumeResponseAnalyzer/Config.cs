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
                Configuration = MessagePackSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllBytes(@".config"));
        }
    }
}
