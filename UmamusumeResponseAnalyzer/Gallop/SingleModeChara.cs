using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class SingleModeChara
	{
		[Key("single_mode_chara_id")]
		public int single_mode_chara_id; // 0x10
		[Key("card_id")]
		public int card_id; // 0x14
		[Key("chara_grade")]
		public int chara_grade; // 0x18
		[Key("speed")]
		public int speed; // 0x1C
		[Key("stamina")]
		public int stamina; // 0x20
		[Key("power")]
		public int power; // 0x24
		[Key("wiz")]
		public int wiz; // 0x28
		[Key("guts")]
		public int guts; // 0x2C
		[Key("vital")]
		public int vital; // 0x30
		[Key("max_speed")]
		public int max_speed; // 0x34
		[Key("max_stamina")]
		public int max_stamina; // 0x38
		[Key("max_power")]
		public int max_power; // 0x3C
		[Key("max_wiz")]
		public int max_wiz; // 0x40
		[Key("max_guts")]
		public int max_guts; // 0x44
		[Key("default_max_speed")]
		public int default_max_speed; // 0x48
		[Key("default_max_stamina")]
		public int default_max_stamina; // 0x4C
		[Key("default_max_power")]
		public int default_max_power; // 0x50
		[Key("default_max_wiz")]
		public int default_max_wiz; // 0x54
		[Key("default_max_guts")]
		public int default_max_guts; // 0x58
		[Key("max_vital")]
		public int max_vital; // 0x5C
		[Key("motivation")]
		public int motivation; // 0x60
		[Key("fans")]
		public int fans; // 0x64
		[Key("rarity")]
		public int rarity; // 0x68
		[Key("race_program_id")]
		public int race_program_id; // 0x6C
		[Key("reserve_race_program_id")]
		public int reserve_race_program_id; // 0x70
		[Key("race_running_style")]
		public int race_running_style; // 0x74
		[Key("is_short_race")]
		public int is_short_race; // 0x78
		[Key("talent_level")]
		public int talent_level; // 0x7C
		[Key("skill_array")]
		public SkillData[] skill_array; // 0x80
		[Key("disable_skill_id_array")]
		public int[] disable_skill_id_array; // 0x88
		[Key("skill_tips_array")]
		public SkillTips[] skill_tips_array; // 0x90
		[Key("support_card_array")]
		public SingleModeSupportCard[] support_card_array; // 0x98
		[Key("succession_trained_chara_id_1")]
		public int succession_trained_chara_id_1; // 0xA0
		[Key("succession_trained_chara_id_2")]
		public int succession_trained_chara_id_2; // 0xA4
		[Key("proper_distance_short")]
		public int proper_distance_short; // 0xA8
		[Key("proper_distance_mile")]
		public int proper_distance_mile; // 0xAC
		[Key("proper_distance_middle")]
		public int proper_distance_middle; // 0xB0
		[Key("proper_distance_long")]
		public int proper_distance_long; // 0xB4
		[Key("proper_running_style_nige")]
		public int proper_running_style_nige; // 0xB8
		[Key("proper_running_style_senko")]
		public int proper_running_style_senko; // 0xBC
		[Key("proper_running_style_sashi")]
		public int proper_running_style_sashi; // 0xC0
		[Key("proper_running_style_oikomi")]
		public int proper_running_style_oikomi; // 0xC4
		[Key("proper_ground_turf")]
		public int proper_ground_turf; // 0xC8
		[Key("proper_ground_dirt")]
		public int proper_ground_dirt; // 0xCC
		[Key("turn")]
		public int turn; // 0xD0
		[Key("skill_point")]
		public int skill_point; // 0xD4
		[Key("short_cut_state")]
		public int short_cut_state; // 0xD8
		[Key("state")]
		public int state; // 0xDC
		[Key("playing_state")]
		public int playing_state; // 0xE0
		[Key("scenario_id")]
		public int scenario_id; // 0xE4
		[Key("route_id")]
		public int route_id; // 0xE8
		[Key("start_time")]
		public string start_time; // 0xF0
		[Key("evaluation_info_array")]
		public EvaluationInfo[] evaluation_info_array; // 0xF0
		[Key("training_level_info_array")]
		public TrainingLevelInfo[] training_level_info_array; // 0xF8
		[Key("nickname_id_array")]
		public int[] nickname_id_array; // 0x100
		[Key("chara_effect_id_array")]
		public int[] chara_effect_id_array; // 0x108
		[Key("route_race_id_array")]
		public int[] route_race_id_array; // 0x110
		[Key("guest_outing_info_array")]
		public GuestOutingInfo[] guest_outing_info_array; // 0x118
		[Key("skill_upgrade_info_array")]
		public SkillUpgradeInfo[] skill_upgrade_info_array;
    }
}
