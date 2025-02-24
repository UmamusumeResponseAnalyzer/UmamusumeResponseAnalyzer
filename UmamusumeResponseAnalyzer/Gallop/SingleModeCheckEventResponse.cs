using Gallop.Mecha;
using MessagePack;
using System;

namespace Gallop
{
    [MessagePackObject]
    public sealed class SingleModeCheckEventResponse : ResponseCommon
    {
        [Key("data")]
        public CommonResponse data;

        [MessagePackObject]
        public class CommonResponse
        {
            [Key("chara_info")]
            public SingleModeChara chara_info;
            [Key("not_up_parameter_info")]
            public NotUpParameterInfo not_up_parameter_info;
            [Key("not_down_parameter_info")]
            public NotDownParameterInfo not_down_parameter_info;
            [Key("home_info")]
            public SingleModeHomeInfo home_info;
            [Key("command_result")]
            public SingleModeCommandResult command_result;
            [Key("unchecked_event_array")]
            public SingleModeEventInfo[] unchecked_event_array;
            [Key("event_effected_factor_array")]
            public SuccessionEffectedFactor[] event_effected_factor_array;
            [Key("race_condition_array")]
            public SingleModeRaceCondition[] race_condition_array;
            [Key("race_start_info")]
            public SingleRaceStartInfo race_start_info;
            [Key("team_data_set")] //青春杯
            public SingleModeTeamDataSet team_data_set; 
            [Key("free_data_set")] //巅峰杯
            public SingleModeFreeDataSet free_data_set; // 0x90
            [Key("live_data_set")] //偶像杯
            public SingleModeTeamDataSet live_data_set; 
            [Key("venus_data_set")] //女神杯
            public SingleModeVenusDataSet venus_data_set;
            [Key("arc_data_set")] //LArc
            public SingleModeArcDataSet arc_data_set;
            [Key("sport_data_set")]
            public SingleModeSportDataSet sport_data_set;
            [Key("cook_data_set")]
            public SingleModeCookDataSet cook_data_set;
            [Key("mecha_data_set")]
            public SingleModeMechaDataSet mecha_data_set;
            [Key("legend_data_set")]
            public SingleModeLegendDataSet legend_data_set;
            [Key("select_index")]
            public int? select_index;
        }
    }
    [MessagePackObject]
    public class SingleModeCommandResult
    {
        [Key("command_id")]
        public int command_id; // 0x10
        [Key("sub_id")]
        public int sub_id; // 0x14
        [Key("result_state")]
        public int result_state; // 0x18
    }
}
