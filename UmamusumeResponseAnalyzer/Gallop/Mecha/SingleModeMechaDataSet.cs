using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop.Mecha
{
    public class SingleModeMechaDataSet
    {
        public SingleModeMechaCommandInfo[] command_info_array;
        public int tuning_point;
        public SingleModeMechaRivalInfo rival_info;
        public SingleModeMechaOverDriveInfo overdrive_info;
        public SingleModeMechaEvaluationInfo[] evaluation_info_array;
        public SingleModeMechaUpgradeRaceResult[] upgrade_race_result_array;
        public SingleModeMechaBoardInfo[] board_info_array;
        public NotUpMechaParameterInfo not_up_macha_parameter_info;
        public SingleModeMechaSubCommandCharaInfo[] sub_command_chara_info_array;
    }
}
