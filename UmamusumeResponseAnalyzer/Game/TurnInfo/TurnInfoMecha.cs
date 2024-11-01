using Gallop;
using Spectre.Console;
using System.ComponentModel.Design;

namespace UmamusumeResponseAnalyzer.Game.TurnInfo
{
    public class TurnInfoMecha : TurnInfo
    {
        public IEnumerable<MechaCommandInfo> CommandInfoArray { get; set; } = [];
        public TurnInfoMecha(SingleModeCheckEventResponse.CommonResponse resp) : base(resp)
        {
            var dataset = resp.mecha_data_set;
            CommandInfoArray = dataset.command_info_array.Select(x => new MechaCommandInfo(resp, this, x.command_id)).ToList();
            CommandInfoArray = CommandInfoArray.Where(x => x.TrainIndex != 0).ToList();
        }
    }

    public class MechaCommandInfo : CommandInfo
    {
        public IEnumerable<(int StatusType, int Value)> PointUpInfoArray { get; set; } = [];
        public bool IsRecommend { get; set; } = false;
        public int EnergyNum { get; set; } = 0;

        public MechaCommandInfo(SingleModeCheckEventResponse.CommonResponse resp, TurnInfo turn, int commandId) : base(resp, turn, commandId)
        {
            var dataset = resp.mecha_data_set;
            var command = dataset.command_info_array.First(x => x.command_id == commandId);
            PointUpInfoArray = command.point_up_info_array.Select(x => (x.status_type, x.value));
            IsRecommend = command.is_recommend;
            EnergyNum = command.energy_num;
        }
    }
}
