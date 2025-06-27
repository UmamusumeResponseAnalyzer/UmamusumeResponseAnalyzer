using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    public class SingleModePioneerDataSet
    {
        public SingleModePioneerInfo pioneer_info;
        public SingleModePioneerCommandInfo[] command_info_array;
        public SingleModePioneerEvaluationInfo[] evaluation_info_array;
        public SingleModePioneerShimaTrainingInfo shima_training_info;
        public SingleModePioneerShimaTrainingGainInfo shima_training_gain_info;
        public SingleModePioneerFacilityInfo[] facility_info_array;
        public SingleModePioneerPlanningInfo[] planning_info_array;
        public SingleModePioneerPointGainInfo[] pioneer_point_gain_info_array;
        public SingleModePioneerCheckPointInfo[] check_point_info_array;
        public SingleModePioneerTrainingExecInfo[] training_exec_info_array;
    }
}
