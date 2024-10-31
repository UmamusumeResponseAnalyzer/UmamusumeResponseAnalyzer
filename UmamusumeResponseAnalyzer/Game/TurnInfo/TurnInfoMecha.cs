using Gallop;
using Spectre.Console;
using System.ComponentModel.Design;

namespace UmamusumeResponseAnalyzer.Game.TurnInfo
{
    internal class TurnInfoMecha : TurnInfo
    {
        public IEnumerable<CommandInfo> CommandInfoArray { get; set; } = [];
        public TurnInfoMecha(SingleModeCheckEventResponse.CommonResponse resp) : base(resp)
        {
            var dataset = resp.mecha_data_set;
            CommandInfoArray = dataset.command_info_array.Select(x => new CommandInfo(resp, this, x.command_id)).ToList();
            CommandInfoArray = CommandInfoArray.Where(x => x.TrainIndex != 0).ToList();
        }
    }
}
