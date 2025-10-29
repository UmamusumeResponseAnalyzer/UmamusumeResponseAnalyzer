using Gallop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    public class SingleModeOnsenCommandInfo
    {
        public int command_type;
        public int command_id;
        public SingleModeParamsIncDecInfo[] params_inc_dec_info_array;
        public SingleModeOnsenDigInfo[] dig_info_array;
    }
}
