using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    public class SingleModeOnsenAssistantCommandInfo
    {
        public int command_type;
        public int is_enable;
        public int[] assistant_partner_id_array;
        public SingleModeParamsIncDecInfo[] params_inc_dec_info_array;
        public SingleModeParamsIncDecInfo[] bonus_params_inc_dec_info_array;
        public SingleModeOnsenDigInfo[] dig_info_array;
    }
}
