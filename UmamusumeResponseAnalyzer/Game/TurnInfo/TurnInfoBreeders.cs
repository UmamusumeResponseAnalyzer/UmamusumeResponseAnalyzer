using Gallop;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Game.TurnInfo
{
    public class TurnInfoBreeders : TurnInfo
    {
        public static readonly int[] TrainIds = [101, 105, 102, 103, 106, 601, 602, 603, 604, 605];
        public static readonly FrozenDictionary<int, int> ToTrainId = new Dictionary<int, int>
        {
            [101] = 101,
            [105] = 105,
            [102] = 102,
            [103] = 103,
            [106] = 106,
            [601] = 101,
            [602] = 105,
            [603] = 102,
            [604] = 103,
            [605] = 106
        }.ToFrozenDictionary();
        public static readonly FrozenDictionary<int, int> ToTrainIndex = new Dictionary<int, int>
        {
            [101] = 0,
            [105] = 1,
            [102] = 2,
            [103] = 3,
            [106] = 4,
            [601] = 0,
            [602] = 1,
            [603] = 2,
            [604] = 3,
            [605] = 4
        }.ToFrozenDictionary();
        public static readonly FrozenDictionary<int, int> XiahesuIds = new Dictionary<int, int>
        {
            [101] = 601,
            [105] = 602,
            [102] = 603,
            [103] = 604,
            [106] = 605
        }.ToFrozenDictionary();

        public static string[] TeamMemberRank = [
            "G", "F", "E", "D", "C", "B", "A", "S", "SS",
            "UG", "UF", "UE", "UD", "UC", "UB", "UA", "US"
        ];
        public List<CommandInfo> CommandInfoArray { get; private set; } = [];
        public Dictionary<int, int> PointGainInfoDictionary { get; private set; } = [];
        public Dictionary<int, SingleModeBreedersTeamMemberInfo> TeamMemberInfoDictionary { get; private set; } = [];
        public Dictionary<int, SingleModeBreedersCommandTeamMemberInfo[]> CommandTeamMemberInfoDictionary { get; private set; } = [];
        public int SpecialTrainingStock = 0;
        public int SpecialTrainingMax = 0;
        public bool SpecialTrainingActivated = false;
        public TurnInfoBreeders(SingleModeCheckEventResponse.CommonResponse resp) : base(resp)
        {
            var breeders = resp.breeders_data_set;
            foreach (var command in breeders.command_info_array.Where(x => x.command_type == 1))
            {
                var commandInfo = new CommandInfo(resp, this, command.command_id, ToTrainIndex, ToTrainId);
                CommandInfoArray.Add(commandInfo);
            }
            SpecialTrainingStock = breeders.team_sp_training_info.stock_num;
            SpecialTrainingMax = breeders.team_sp_training_info.stock_max;
            SpecialTrainingActivated = breeders.team_sp_training_info.activated_state == 1;
            TeamMemberInfoDictionary = breeders.team_member_info_array.ToDictionary(x => x.chara_id, x => x);
            CommandTeamMemberInfoDictionary = breeders.command_info_array.ToDictionary(x => x.command_id, x => x.team_member_info_array);
        }
    }
}
