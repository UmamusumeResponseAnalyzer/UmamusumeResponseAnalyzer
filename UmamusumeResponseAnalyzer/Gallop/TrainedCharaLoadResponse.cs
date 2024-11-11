using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
	public class TrainedCharaLoadResponse : ResponseCommon
	{
		[Key("data")]
		public CommonResponse data;
		[MessagePackObject]
		public class CommonResponse
		{
			[Key("trained_chara_array")]
			public TrainedChara[] trained_chara_array;
			[Key("trained_chara_favorite_array")]
			public TrainedCharaFavorite[] trained_chara_favorite_array;
			[Key("room_match_entry_chara_id_array")]
			public int[] room_match_entry_chara_id_array;
		}
	}
	[MessagePackObject]
	public class TrainedChara // TypeDefIndex: 7555
	{
		[Key("viewer_id")]
		public long viewer_id; // 0x10
		[Key("trained_chara_id")]
		public int trained_chara_id; // 0x18
		[Key("owner_viewer_id")]
		public long owner_viewer_id; // 0x20
		[Key("use_type")]
		public int use_type; // 0x28
		[Key("card_id")]
		public int card_id; // 0x2C
		[Key("name")]
		public string name; // 0x30
		[Key("stamina")]
		public int stamina; // 0x38
		[Key("speed")]
		public int speed; // 0x3C
		[Key("power")]
		public int power; // 0x40
		[Key("guts")]
		public int guts; // 0x44
		[Key("wiz")]
		public int wiz; // 0x48
		[Key("fans")]
		public int fans; // 0x4C
		[Key("rank_score")]
		public int rank_score; // 0x50
		[Key("rank")]
		public int rank; // 0x54
		[Key("proper_distance_short")]
		public int proper_distance_short; // 0x58
		[Key("proper_distance_mile")]
		public int proper_distance_mile; // 0x5C
		[Key("proper_distance_middle")]
		public int proper_distance_middle; // 0x60
		[Key("proper_distance_long")]
		public int proper_distance_long; // 0x64
		[Key("proper_running_style_nige")]
		public int proper_running_style_nige; // 0x68
		[Key("proper_running_style_senko")]
		public int proper_running_style_senko; // 0x6C
		[Key("proper_running_style_sashi")]
		public int proper_running_style_sashi; // 0x70
		[Key("proper_running_style_oikomi")]
		public int proper_running_style_oikomi; // 0x74
		[Key("proper_ground_turf")]
		public int proper_ground_turf; // 0x78
		[Key("proper_ground_dirt")]
		public int proper_ground_dirt; // 0x7C
		[Key("succession_num")]
		public int succession_num; // 0x80
		[Key("is_locked")]
		public int is_locked; // 0x84
		[Key("rarity")]
		public int rarity; // 0x88
		[Key("talent_level")]
		public int talent_level; // 0x8C
		[Key("chara_grade")]
		public int chara_grade; // 0x90
		[Key("running_style")]
		public int running_style; // 0x94
		[Key("nickname_id")]
		public int nickname_id; // 0x98
		[Key("wins")]
		public int wins; // 0x9C
		[Key("skill_array")]
		public SkillData[] skill_array; // 0xA0
		[Key("support_card_list")]
		public TrainedCharaSupportCardList[] support_card_list; // 0xA8
		[Key("is_saved")]
		public int is_saved; // 0xB0
		[Key("race_result_list")]
		public TrainedCharaRaceResult[] race_result_list; // 0xB8
		[Key("win_saddle_id_array")]
		public int[] win_saddle_id_array; // 0xC0
		[Key("nickname_id_array")]
		public int[] nickname_id_array; // 0xC8
		//[Key("factor_id_array")]
		//public int[] factor_id_array; // 0xD0
		[Key("factor_info_array")]
		public FactorInfo[] factor_info_array; // 0xD8
		[Key("factor_extend_array")]
		public FactorExtendInfo[] factor_extend_array;
        [Key("succession_chara_array")]
		public SuccessionChara[] succession_chara_array; // 0xE0
		[Key("succession_history_array")]
		public SuccessionHistory[] succession_history_array; // 0xE8
		[Key("scenario_id")]
		public int scenario_id; // 0xF0
		[Key("create_time")]
		public string create_time;
	}
	[MessagePackObject]
	public class TrainedCharaFavorite // TypeDefIndex: 7556
	{
		[Key("trained_chara_id")]
		public int trained_chara_id; // 0x10
		[Key("icon_type")]
		public int icon_type; // 0x14
		[Key("memo")]
		public string memo; // 0x18
	}
	[MessagePackObject]
	public class TrainedCharaSupportCardList // TypeDefIndex: 7559
	{
		[Key("position")]
		public int position; // 0x10
		[Key("support_card_id")]
		public int support_card_id; // 0x14
		[Key("exp")]
		public int exp; // 0x18
		[Key("limit_break_count")]
		public int limit_break_count; // 0x1C
	}
	[MessagePackObject]
	public class TrainedCharaRaceResult // TypeDefIndex: 7558
	{
		[Key("turn")]
		public int turn; // 0x10
		[Key("program_id")]
		public int program_id; // 0x14
		[Key("weather")]
		public int weather; // 0x18
		[Key("ground_condition")]
		public int ground_condition; // 0x1C
		[Key("running_style")]
		public int running_style; // 0x20
		[Key("result_rank")]
		public int result_rank; // 0x24
	}
	[MessagePackObject]
	public class SuccessionChara // TypeDefIndex: 7522
	{
		[Key("position_id")]
		public int position_id; // 0x10
		[Key("card_id")]
		public int card_id; // 0x14
		[Key("rank")]
		public int rank; // 0x18
		[Key("rarity")]
		public int rarity; // 0x1C
		[Key("talent_level")]
		public int talent_level; // 0x20
		//[Key("factor_id_array")]
		//public int[] factor_id_array; // 0x28
		[Key("factor_info_array")]
		public FactorInfo[] factor_info_array; // 0x30
		[Key("win_saddle_id_array")]
		public int[] win_saddle_id_array; // 0x38
		[Key("owner_viewer_id")]
		public long owner_viewer_id; // 0x40
		[Key("user_info_summary")]
		public UserInfoAtFriend user_info_summary; // 0x48
	}
	[MessagePackObject]
	public class UserInfoAtFriend // TypeDefIndex: 7580
	{
		[Key("viewer_id")]
		public long viewer_id; // 0x10
		[Key("name")]
		public string name; // 0x18
		[Key("honor_id")]
		public int honor_id; // 0x20
		[Key("last_login_time")]
		public string last_login_time; // 0x28
		[Key("leader_chara_id")]
		public int leader_chara_id; // 0x30
		[Key("support_card_id")]
		public int support_card_id; // 0x34
		[Key("comment")]
		public string comment; // 0x38
		[Key("fan")]
		public ulong fan; // 0x40
		[Key("directory_level")]
		public int directory_level; // 0x48
		[Key("rank_score")]
		public int rank_score; // 0x4C
		[Key("team_stadium_win_count")]
		public int team_stadium_win_count; // 0x50
		[Key("single_mode_play_count")]
		public int single_mode_play_count; // 0x54
		[Key("team_evaluation_point")]
		public int team_evaluation_point; // 0x58
		[Key("best_team_evaluation_point")]
		public int best_team_evaluation_point; // 0x5C
		[Key("friend_state")]
		public int friend_state; // 0x60
		[Key("circle_info")]
		public CircleInfoAtFriend circle_info; // 0x68
		[Key("circle_user")]
		public CircleUser circle_user; // 0x70
		[Key("user_support_card")]
		public UserSupportCardAtFriend user_support_card; // 0x78
		[Key("leader_chara_dress_id")]
		public int leader_chara_dress_id; // 0x80
		[Key("user_trained_chara")]
		public UserTrainedCharaAtFriend user_trained_chara; // 0x88
        [Key("total_login_day_count")]
        public int total_login_day_count;
	}
	[MessagePackObject]
	public class CircleInfoAtFriend // TypeDefIndex: 7335
	{
		[Key("circle_id")]
		public int circle_id; // 0x10
		[Key("name")]
		public string name; // 0x18
		[Key("monthly_rank")]
		public int monthly_rank; // 0x20
	}
	[MessagePackObject]
	public class CircleUser // TypeDefIndex: 7342
	{
		[Key("viewer_id")]
		public long viewer_id; // 0x10
		[Key("circle_id")]
		public int circle_id; // 0x18
		[Key("membership")]
		public int membership; // 0x1C
		[Key("join_time")]
		public string join_time; // 0x20
		[Key("penalty_end_time")]
		public string penalty_end_time; // 0x28
		[Key("item_request_end_time")]
		public string item_request_end_time; // 0x30
	}
	[MessagePackObject]
	public class UserSupportCardAtFriend // TypeDefIndex: 7586
	{
		[Key("support_card_id")]
		public int support_card_id; // 0x10
		[Key("exp")]
		public int exp; // 0x14
		[Key("limit_break_count")]
		public int limit_break_count;
	}
	[MessagePackObject]
	public class UserTrainedCharaAtFriend // TypeDefIndex: 7589
	{
		[Key("viewer_id")]
		public long viewer_id; // 0x10
		[Key("trained_chara_id")]
		public int trained_chara_id; // 0x18
		[Key("card_id")]
		public int card_id; // 0x1C
		[Key("rank_score")]
		public int rank_score; // 0x20
		[Key("rank")]
		public int rank; // 0x24
		[Key("proper_distance_short")]
		public int proper_distance_short; // 0x28
		[Key("proper_distance_mile")]
		public int proper_distance_mile; // 0x2C
		[Key("proper_distance_middle")]
		public int proper_distance_middle; // 0x30
		[Key("proper_distance_long")]
		public int proper_distance_long; // 0x34
		[Key("proper_running_style_nige")]
		public int proper_running_style_nige; // 0x38
		[Key("proper_running_style_senko")]
		public int proper_running_style_senko; // 0x3C
		[Key("proper_running_style_sashi")]
		public int proper_running_style_sashi; // 0x40
		[Key("proper_running_style_oikomi")]
		public int proper_running_style_oikomi; // 0x44
		[Key("proper_ground_turf")]
		public int proper_ground_turf; // 0x48
		[Key("proper_ground_dirt")]
		public int proper_ground_dirt; // 0x4C
		[Key("rarity")]
		public int rarity; // 0x50
		[Key("talent_level")]
		public int talent_level; // 0x54
		[Key("register_time")]
		public string register_time; // 0x58
		[Key("factor_id_array")]
		public int[] factor_id_array; // 0x60
		[Key("factor_info_array")]
		public FactorInfo[] factor_info_array; // 0x68
		[Key("skill_count")]
		public int skill_count; // 0x70
	}
	[MessagePackObject]
	public class SuccessionHistory // TypeDefIndex: 7524
	{
		[Key("id")]
		public int id; // 0x10
		[Key("viewer_id")]
		public long viewer_id; // 0x18
		[Key("trained_chara_id")]
		public int trained_chara_id; // 0x20
		[Key("hisotry_type")]
		public int hisotry_type; // 0x24
		[Key("succession_card_id")]
		public int succession_card_id; // 0x28
		[Key("date")]
		public int date; // 0x2C
		[Key("user_name")]
		public string user_name; // 0x30
		[Key("circle_name")]
		public string circle_name; // 0x38
	}
}