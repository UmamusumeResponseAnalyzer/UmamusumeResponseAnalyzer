using Gallop.Mecha;
using MessagePack;
using System;

namespace Gallop
{
    public sealed class SingleModeCheckEventResponse : ResponseCommon
    {
        public CommonResponse data;

        public class CommonResponse
        {
            public SingleModeChara chara_info;
            public NotUpParameterInfo not_up_parameter_info;
            public NotDownParameterInfo not_down_parameter_info;
            public SingleModeHomeInfo home_info;
            public SingleModeCommandResult command_result;
            public SingleModeEventInfo[] unchecked_event_array;
            public SuccessionEffectedFactor[] event_effected_factor_array;
            public SingleModeRaceCondition[] race_condition_array;
            public SingleRaceStartInfo race_start_info;
            public SingleModeTeamDataSet team_data_set;
            public SingleModeFreeDataSet free_data_set;
            public SingleModeTeamDataSet live_data_set;
            public SingleModeVenusDataSet venus_data_set;
            public SingleModeArcDataSet arc_data_set;
            public SingleModeSportDataSet sport_data_set;
            public SingleModeCookDataSet cook_data_set;
            public SingleModeMechaDataSet mecha_data_set;
            public SingleModeLegendDataSet legend_data_set;
            public SingleModePioneerDataSet pioneer_data_set;
            public SingleModeOnsenDataSet onsen_data_set;
            public int? select_index;
        }
    }
    public class SingleModeCommandResult
    {
        public int command_id;
        public int sub_id;
        public int result_state;
    }
}
