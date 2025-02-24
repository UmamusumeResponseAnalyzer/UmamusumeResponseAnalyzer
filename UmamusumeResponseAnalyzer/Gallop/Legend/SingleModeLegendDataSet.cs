using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    public class SingleModeLegendDataSet
    {
        public SingleModeLegendCommandInfo[] command_info_array;
        public SingleModeLegendEvaluationInfo[] evaluation_info_array;
        public SingleModeLegendGauge[] gauge_count_array;
        public SingleModeLegendBuffInfo[] buff_info_array;
        public int[] obtainable_buff_id_array;
        public SingleModeLegendMasterlyInfo masterly_bonus_info;
        public SingleModeLegendRaceHistory[] race_history_array;
        public SingleModeLegendNotUpParameterInfo not_up_parameter_info;
        public SingleModeLegendNotUpBuffParameterInfo not_up_buff_parameter_info;
        public SingleModeLegendNotDownParameterInfo not_down_parameter_info;
        public int[] activated_buff_id_array;
        public int route_id;
        public SingleModeLegendCmInfo cm_info;
        public SingleModeLegendPopularityInfo popularity_info;
        public bool is_appear_legend;
    }
}
