using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop.Mecha
{
    public class SingleModeMechaCommandInfo
    {
        public int command_type;
        public int command_id;
        public SingleModeParamsIncDecInfo[] params_inc_dec_info_array;
        public SingleModeMechaPointUpInfo[] point_up_info_array;
        public bool is_recommend;
        public int energy_num;
    }
}
