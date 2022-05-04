using Gallop;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer
{
    [MessagePackObject]
    public class SingleModeFreeDataSet // TypeDefIndex: 8220
    {
        [Key("shop_id")]
        public int shop_id; // 0x10
        [Key("sale_value")]
        public int sale_value; // 0x14
        [Key("win_points")]
        public int win_points; // 0x18
        [Key("gained_coin_num")]
        public int gained_coin_num; // 0x1C
        [Key("coin_num")]
        public int coin_num; // 0x20
        [Key("twinkle_race_ranking")]
        public int twinkle_race_ranking; // 0x24
        [Key("user_item_info_array")]
        public SingleModeFreeUserItem[] user_item_info_array; // 0x28
        [Key("pick_up_item_info_array")]
        public SingleModeFreePickUpItem[] pick_up_item_info_array; // 0x30
        [Key("twinkle_race_npc_info_array")]
        public SingleModeFreeTwinkleRaceNpcInfo[] twinkle_race_npc_info_array; // 0x38
        [Key("item_effect_array")]
        public SingleModeFreeItemEffect[] item_effect_array; // 0x40
        [Key("twinkle_race_npc_result_array")]
        public SingleModeTwikleRaceNpcResult[] twinkle_race_npc_result_array; // 0x48
        [Key("rival_race_info_array")]
        public SingleModeRivalRaceInfo[] rival_race_info_array; // 0x50
        [Key("command_info_array")]
        public SingleModeFreeCommandInfo[] command_info_array; // 0x58
        [Key("unchecked_event_achievement_id")]
        public int unchecked_event_achievement_id; // 0x60
    }
    [MessagePackObject]
    public class SingleModeFreeUserItem // TypeDefIndex: 8226
    {
        [Key("item_id")]
        public int item_id; // 0x10
        [Key("num")]
        public int num; // 0x14
    }
    [MessagePackObject]
    public class SingleModeFreePickUpItem // TypeDefIndex: 8223
    {
        [Key("shop_item_id")]
        public int shop_item_id; // 0x10
        [Key("item_id")]
        public int item_id; // 0x14
        [Key("coin_num")]
        public int coin_num; // 0x18
        [Key("original_coin_num")]
        public int original_coin_num; // 0x1C
        [Key("item_buy_num")]
        public int item_buy_num; // 0x20
        [Key("limit_buy_count")]
        public int limit_buy_count; // 0x24
        [Key("limit_turn")]
        public int limit_turn; // 0x28
    }
    [MessagePackObject]
    public class SingleModeFreeTwinkleRaceNpcInfo // TypeDefIndex: 8224
    {
        [Key("npc_id")]
        public int npc_id; // 0x10
        [Key("chara_id")]
        public int chara_id; // 0x14
        [Key("dress_id")]
        public int dress_id; // 0x18
        [Key("talent_level")]
        public int talent_level; // 0x1C
        [Key("win_points")]
        public int win_points; // 0x20
        [Key("speed")]
        public int speed; // 0x24
        [Key("stamina")]
        public int stamina; // 0x28
        [Key("power")]
        public int power; // 0x2C
        [Key("guts")]
        public int guts; // 0x30
        [Key("wiz")]
        public int wiz; // 0x34
        [Key("proper_distance_short")]
        public int proper_distance_short; // 0x38
        [Key("proper_distance_mile")]
        public int proper_distance_mile; // 0x3C
        [Key("proper_distance_middle")]
        public int proper_distance_middle; // 0x40
        [Key("proper_distance_long")]
        public int proper_distance_long; // 0x44
        [Key("proper_running_style_nige")]
        public int proper_running_style_nige; // 0x48
        [Key("proper_running_style_senko")]
        public int proper_running_style_senko; // 0x4C
        [Key("proper_running_style_sashi")]
        public int proper_running_style_sashi; // 0x50
        [Key("proper_running_style_oikomi")]
        public int proper_running_style_oikomi; // 0x54
        [Key("proper_ground_turf")]
        public int proper_ground_turf; // 0x58
        [Key("proper_ground_dirt")]
        public int proper_ground_dirt; // 0x5C
        [Key("skill_array")]
        public SkillData[] skill_array; // 0x60
    }
    [MessagePackObject]
    public class SingleModeFreeItemEffect // TypeDefIndex: 8222
    {
        [Key("use_id")]
        public int use_id; // 0x10
        [Key("item_id")]
        public int item_id; // 0x14
        [Key("effect_type")]
        public int effect_type; // 0x18
        [Key("effect_value_1")]
        public int effect_value_1; // 0x1C
        [Key("effect_value_2")]
        public int effect_value_2; // 0x20
        [Key("effect_value_3")]
        public int effect_value_3; // 0x24
        [Key("effect_value_4")]
        public int effect_value_4; // 0x28
        [Key("begin_turn")]
        public int begin_turn; // 0x2C
        [Key("end_turn")]
        public int end_turn; // 0x30
    }
    [MessagePackObject]
    public class SingleModeTwikleRaceNpcResult // TypeDefIndex: 8268
    {
        [Key("turn")]
        public int turn; // 0x10
        [Key("program_id")]
        public int program_id; // 0x14
        [Key("race_result_array")]
        public SingleModeNpcResult[] race_result_array; // 0x18
    }
    [MessagePackObject]
    public class SingleModeRivalRaceInfo // TypeDefIndex: 8246
    {
        [Key("program_id")]
        public int program_id; // 0x10
        [Key("chara_id")]
        public int chara_id; // 0x14
    }
    [MessagePackObject]
    public class SingleModeFreeCommandInfo // TypeDefIndex: 8219
    {
        [Key("command_type")]
        public int command_type; // 0x10
        [Key("command_id")]
        public int command_id; // 0x14
        [Key("params_inc_dec_info_array")]
        public SingleModeParamsIncDecInfo[] params_inc_dec_info_array; // 0x18
    }
    [MessagePackObject]
    public class SingleModeNpcResult // TypeDefIndex: 8231
    {
        [Key("npc_id")]
        public int npc_id; // 0x10
        [Key("result_rank")]
        public int result_rank; // 0x14
    }
}
