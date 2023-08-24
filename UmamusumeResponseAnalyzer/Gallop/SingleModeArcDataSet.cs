using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SingleModeArcDataSet
    {
        [Key("arc_info")]
        public SingleModeArcInfo arc_info; // 0x10
        [Key("arc_rival_array")]
        public SingleModeArcRival[] arc_rival_array; // 0x18
        [Key("rival_race_info_array")]
        public SingleModeRivalRaceInfo[] rival_race_info_array; // 0x20
        [Key("selection_info")]
        public SingleModeArcSelectionInfo selection_info; // 0x28
        [Key("race_history_array")]
        public SingleModeArcRaceHistory[] race_history_array; // 0x30
        [Key("command_info_array")]
        public SingleModeArcCommandInfo[] command_info_array; // 0x38
        [Key("evaluation_info_array")]
        public ArcEvaluationInfo[] evaluation_info_array; // 0x40
        [Key("not_up_arc_parameter_info")]
        public NotUpArcParameterInfo not_up_arc_parameter_info; // 0x48
    }
    [MessagePackObject]
    public class SingleModeArcInfo
    {
        [Key("approval_rate")]
        public int approval_rate; // 0x10
        [Key("potential_array")]
        public ArcPotential[] potential_array; // 0x18
        [Key("global_exp")]
        public int global_exp; // 0x20
        [Key("sp_tag_boost_type")]
        public int sp_tag_boost_type; // 0x24
    }
    [MessagePackObject]
    public class SingleModeArcRival
    {
        [Key("chara_id")]
        public int chara_id; // 0x10
        [Key("speed")]
        public int speed; // 0x14
        [Key("stamina")]
        public int stamina; // 0x18
        [Key("power")]
        public int power; // 0x1c
        [Key("guts")]
        public int guts; // 0x20
        [Key("wiz")]
        public int wiz; // 0x24
        [Key("command_id")]
        public int command_id; // 0x28
        [Key("rival_boost")]
        public int rival_boost; // 0x2c
        [Key("star_lv")]
        public int star_lv; // 0x30
        [Key("rank")]
        public int rank; // 0x34
        [Key("approval_point")]
        public int approval_point; // 0x38
        [Key("potential_array")]
        public ArcRivalPotential[] potential_array; // 0x40
        [Key("selection_peff_array")]
        public ArcSelectionPeff[] selection_peff_array; // 0x48
    }
    [MessagePackObject]
    public class SingleModeArcSelectionInfo
    {
        [Key("all_win_approval_point")]
        public int all_win_approval_point; // 0x10
        [Key("params_inc_dec_info_array")]
        public SingleModeParamsIncDecInfo[] params_inc_dec_info_array; // 0x18
        [Key("selection_rival_info_array")]
        public SingleModeArcSelectionRivalInfo[] selection_rival_info_array; // 0x20
        [Key("is_special_match")]
        public int is_special_match; // 0x28
        [Key("bonus_params_inc_dec_info_array")]
        public SingleModeParamsIncDecInfo[] bonus_params_inc_dec_info_array; // 0x30
    }
    [MessagePackObject]
    public class SingleModeArcRaceHistory
    {
        [Key("race_num")]
        public int race_num; // 0x10
        [Key("turn")]
        public int turn; // 0x14
        [Key("result_rank")]
        public int result_rank; // 0x18
    }
    [MessagePackObject]
    public class SingleModeArcCommandInfo
    {
        [Key("command_type")]
        public int command_type; // 0x10
        [Key("command_id")]
        public int command_id; // 0x14
        [Key("params_inc_dec_info_array")]
        public SingleModeParamsIncDecInfo[] params_inc_dec_info_array; // 0x18
        [Key("add_global_exp")]
        public int add_global_exp; // 0x20
    }
    [MessagePackObject]
    public class ArcEvaluationInfo
    {
        [Key("target_id")]
        public int target_id; // 0x10
        [Key("chara_id")]
        public int chara_id; // 0x14
    }
    [MessagePackObject]
    public class NotUpArcParameterInfo
    {
        [Key("rival_boost_chara_id_array")]
        public int[] rival_boost_chara_id_array; // 0x10
        [Key("all_rival_boost_flag")]
        public bool all_rival_boost_flag; // 0x18
    }
    [MessagePackObject]
    public class ArcPotential
    {
        [Key("potential_id")]
        public int potential_id; // 0x10
        [Key("level")]
        public int level; // 0x14
        [Key("progress_array")]
        public SingleModeArcPotentialProgress[] progress_array; // 0x18
    }
    [MessagePackObject]
    public class ArcRivalPotential
    {
        [Key("potential_id")]
        public int potential_id; // 0x10
        [Key("level")]
        public int level; // 0x14
    }
    [MessagePackObject]
    public class ArcSelectionPeff
    {
        [Key("effect_num")]
        public int effect_num; // 0x10
        [Key("effect_group_id")]
        public int effect_group_id; // 0x14
        [Key("effect_value")]
        public int effect_value; // 0x18
    }
    [MessagePackObject]
    public class SingleModeArcSelectionRivalInfo
    {
        [Key("chara_id")]
        public int chara_id; // 0x10
        [Key("mark")]
        public int mark; // 0x14
        [Key("win_approval_point")]
        public int win_approval_point; // 0x18
        [Key("lose_approval_point")]
        public int lose_approval_point; // 0x1c
        [Key("rival_win_approval_point")]
        public int rival_win_approval_point; // 0x20
        [Key("rival_lose_approval_point")]
        public int rival_lose_approval_point; // 0x24
    }
    [MessagePackObject]
    public class SingleModeArcPotentialProgress
    {
        [Key("condition_id")]
        public int condition_id; // 0x10
        [Key("total_count")]
        public int total_count; // 0x14
        [Key("current_count")]
        public int current_count; // 0x18
    }
}
