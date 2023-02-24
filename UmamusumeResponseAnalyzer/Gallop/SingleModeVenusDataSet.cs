using Gallop;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SingleModeVenusDataSet
    {
        [Key("race_start_info")]
        public SingleRaceStartInfo race_start_info; // 0x10
        [Key("race_scenario")]
        public string race_scenario; // 0x18
        [Key("command_info_array")]
        public SingleModeVenusCommandInfo[] command_info_array; // 0x20
        [Key("evaluation_info_array")]
        public SingleModeVenusEvaluationInfo[] evaluation_info_array; // 0x28
        [Key("spirit_info_array")]
        public SingleModeVenusSpiritInfo[] spirit_info_array; // 0x30
        [Key("venus_spirit_active_effect_info_array")]
        public SingleModeVenusActiveSpiritEffect[] venus_spirit_active_effect_info_array; // 0x38
        [Key("venus_chara_info_array")]
        public SingleModeVenusCharaInfo[] venus_chara_info_array; // 0x40
        [Key("venus_chara_command_info_array")]
        public SingleModeVenusCharaCommandInfo[] venus_chara_command_info_array; // 0x48
        [Key("venus_race_condition")]
        public SingleModeRaceCondition venus_race_condition; // 0x50
        [Key("venus_race_history_array")]
        public SingleModeVenusRaceHistory[] venus_race_history_array; // 0x58
        [Key("race_reward_info")]
        public CharaRaceReward race_reward_info; // 0x60
        [Key("live_item_id")]
        public int live_item_id; // 0x68
    }
    [MessagePackObject]
    public class SingleModeVenusCommandInfo
    {
        [Key("command_type")]
        public int command_type; // 0x10
        [Key("command_id")]
        public int command_id; // 0x14
        [Key("params_inc_dec_info_array")]
        public SingleModeParamsIncDecInfo[] params_inc_dec_info_array; // 0x18
    }
    [MessagePackObject]
    public class SingleModeVenusEvaluationInfo
    {
        [Key("target_id")]
        public int target_id; // 0x10
        [Key("chara_id")]
        public int chara_id; // 0x14
        [Key("member_state")]
        public int member_state; // 0x18
    }
    [MessagePackObject]
    public class SingleModeVenusSpiritInfo
    {
        [Key("spirit_num")]
        public int spirit_num; // 0x10
        [Key("spirit_id")]
        public int spirit_id; // 0x14
        [Key("effect_group_id")]
        public int effect_group_id; // 0x18
    }
    [MessagePackObject]
    public class SingleModeVenusActiveSpiritEffect
    {
        [Key("chara_id")]
        public int chara_id; // 0x10
        [Key("effect_group_id")]
        public int effect_group_id; // 0x14
        [Key("begin_turn")]
        public int begin_turn; // 0x18
        [Key("end_turn")]
        public int end_turn; // 0x1C
    }
    [MessagePackObject]
    public class SingleModeVenusCharaInfo
    {
        [Key("chara_id")]
        public int chara_id; // 0x10
        [Key("venus_level")]
        public int venus_level; // 0x14
    }
    [MessagePackObject]
    public class SingleModeVenusCharaCommandInfo
    {
        [Key("command_type")]
        public int command_type; // 0x10
        [Key("command_id")]
        public int command_id; // 0x14
        [Key("spirit_id")]
        public int spirit_id; // 0x18
        [Key("is_boost")]
        public int is_boost; // 0x1C
    }
    [MessagePackObject]
    public class SingleModeVenusRaceHistory
    {
        [Key("race_num")]
        public int race_num; // 0x10
        [Key("turn")]
        public int turn; // 0x14
        [Key("result_rank")]
        public int result_rank; // 0x18
    }
}
