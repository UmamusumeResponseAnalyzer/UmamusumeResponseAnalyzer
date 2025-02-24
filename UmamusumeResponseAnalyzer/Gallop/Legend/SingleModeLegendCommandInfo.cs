using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    public class SingleModeLegendCommandInfo
    {
        public int command_type;
        public int command_id;
        public int legend_id;
        public int gain_gauge;
        public SingleModeParamsIncDecInfo[] params_inc_dec_info_array;
        public SingleModeLegend9048GaugeGain[] friend_gauge_gain_array;
    }
}
