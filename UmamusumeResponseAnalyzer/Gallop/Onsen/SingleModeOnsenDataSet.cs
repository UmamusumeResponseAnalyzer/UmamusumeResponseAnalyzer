using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    public class SingleModeOnsenDataSet
    {
        public SingleModeOnsenCommandInfo[] command_info_array;
        public SingleModeOnsenBathingInfo bathing_info;
        public SingleModeOnsenInfo[] onsen_info_array;
        public SingleModeOnsenDigEffectInfo[] dig_effect_info_array;
        public int[] dug_onsen_id_array;
        public int[] effected_onsen_id_array;
        public SingleModeOnsenDigEffectInfo[] level_up_dig_effect_info_array;
        public SingleModeOnsenEvaluationInfo[] evaluation_info_array;
        public SingleModeOnsenAssistantCommandInfo assistant_command_info;
        public int ryokan_rank;
        public int ryokan_rank_clear_state;
        public SingleModeOnsenCheckDugResult[] check_dug_result_array;
        public SingleModeOnsenNotUpParameterInfo not_up_parameter_info;
        public SingleModeOnsenOutingEffect[] outing_effect_array;
    }
}
