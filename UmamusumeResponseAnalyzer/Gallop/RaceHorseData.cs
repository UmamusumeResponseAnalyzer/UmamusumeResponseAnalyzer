using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class RaceHorseData
	{
		[Key("viewer_id")]
		public long viewer_id; // 0x10
		[Key("owner_viewer_id")]
		public long owner_viewer_id; // 0x18
		[Key("trainer_name")]
		public string trainer_name; // 0x20
		[Key("owner_trainer_name")]
		public string owner_trainer_name; // 0x28
		[Key("single_mode_chara_id")]
		public int single_mode_chara_id; // 0x30
		[Key("trained_chara_id")]
		public int trained_chara_id; // 0x34
		[Key("nickname_id")]
		public int nickname_id; // 0x38
		[Key("card_id")]
		public int? card_id; // 0x3C
		[Key("chara_id")]
		public int chara_id; // 0x40
		[Key("rarity")]
		public int rarity; // 0x44
		[Key("talent_level")]
		public int talent_level; // 0x48
		[Key("frame_order")]
		public int frame_order; // 0x4C
		[Key("skill_array")]
		public SkillData[] skill_array; // 0x50
		[Key("stamina")]
		public int stamina; // 0x58
		[Key("speed")]
		public int speed; // 0x5C
		[Key("pow")]
		public int pow; // 0x60
		[Key("guts")]
		public int guts; // 0x64
		[Key("wiz")]
		public int wiz; // 0x68
		[Key("running_style")]
		public int running_style; // 0x6C
		[Key("race_dress_id")]
		public int race_dress_id; // 0x70
		[Key("chara_color_type")]
		public int chara_color_type; // 0x74
		[Key("npc_type")]
		public int npc_type; // 0x78
		[Key("final_grade")]
		public int final_grade; // 0x7C
		[Key("popularity")]
		public int popularity; // 0x80
		[Key("popularity_mark_rank_array")]
		public int[] popularity_mark_rank_array; // 0x88
		[Key("proper_distance_short")]
		public int proper_distance_short; // 0x90
		[Key("proper_distance_mile")]
		public int proper_distance_mile; // 0x94
		[Key("proper_distance_middle")]
		public int proper_distance_middle; // 0x98
		[Key("proper_distance_long")]
		public int proper_distance_long; // 0x9C
		[Key("proper_running_style_nige")]
		public int proper_running_style_nige; // 0xA0
		[Key("proper_running_style_senko")]
		public int proper_running_style_senko; // 0xA4
		[Key("proper_running_style_sashi")]
		public int proper_running_style_sashi; // 0xA8
		[Key("proper_running_style_oikomi")]
		public int proper_running_style_oikomi; // 0xAC
		[Key("proper_ground_turf")]
		public int proper_ground_turf; // 0xB0
		[Key("proper_ground_dirt")]
		public int proper_ground_dirt; // 0xB4
		[Key("motivation")]
		public int motivation; // 0xB8
		[Key("mob_id")]
		public int mob_id; // 0xBC
		[Key("win_saddle_id_array")]
		public int[] win_saddle_id_array; // 0xC0
		[Key("race_result_array")]
		public RaceHorseDataRaceResult[] race_result_array; // 0xC8
		[Key("team_id")]
		public int team_id; // 0xD0
		[Key("team_member_id")]
		public int team_member_id; // 0xD4
		[Key("item_id_array")]
		public int[] item_id_array; // 0xD8
		[Key("motivation_change_flag")]
		public int motivation_change_flag; // 0xE0
		[Key("frame_order_change_flag")]
		public int frame_order_change_flag; // 0xE4
		[Key("team_rank")]
		public int team_rank; // 0xE8
		[Key("single_mode_win_count")]
		public int single_mode_win_count;
	}
}
