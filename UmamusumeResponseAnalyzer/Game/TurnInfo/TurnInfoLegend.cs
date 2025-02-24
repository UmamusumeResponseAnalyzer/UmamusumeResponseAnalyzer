using Gallop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Localization.CommandInfo;

namespace UmamusumeResponseAnalyzer.Game.TurnInfo
{
    internal class TurnInfoLegend : TurnInfo
    {
        /// command_type 4 休息？
        /// command_type 7 比赛？
        /// command_type 3 出门
        /// 
        /// <summary>
        /// 9048->粉；9046->蓝；9047->绿；
        /// </summary>
        public Dictionary<int, (int Legend, int Gauge)> CommandGauges { get; set; } = [];
        public IEnumerable<CommandInfo> CommandInfoArray { get; set; } = [];
        public Dictionary<int, int> GaugeCountDictonary { get; set; } = [];
        public TurnInfoLegend(SingleModeCheckEventResponse.CommonResponse resp) : base(resp)
        {
            var legend = resp.legend_data_set;
            foreach (var i in legend.command_info_array)
            {
                CommandGauges.TryAdd(i.command_id, (i.legend_id, i.gain_gauge));
            }
            GaugeCountDictonary = legend.gauge_count_array.ToDictionary(x => x.legend_id, x => x.count);
            CommandInfoArray = legend.command_info_array.Where(x => x.command_id is not 0 and not 701 and not 401 and not 801).Select(x => new CommandInfo(resp, this, x.command_id));
        }
    }
}
