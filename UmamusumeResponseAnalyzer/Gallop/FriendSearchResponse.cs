using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
	[MessagePackObject]
    public class FriendSearchResponse
	{
		[Key("data")]
		public CommonResponse data; // 0x18

		[MessagePackObject]
		public class CommonResponse
		{
			[Key("friend_info")]
			public UserFriend friend_info; // 0x10
			[Key("user_info_summary")]
			public UserInfoAtFriend user_info_summary; // 0x18
			[Key("practice_partner_info")]
			public TrainedChara practice_partner_info; // 0x20
			[Key("directory_chara_info")]
			public TrainedChara[] directory_chara_info; // 0x28
			[Key("directory_card_array")]
			public DirectoryCard[] directory_card_array; // 0x30
			[Key("support_card_data")]
			public UserSupportCard support_card_data; // 0x38
			[Key("trophy_num_info")]
			public TrophyNumInfo trophy_num_info; // 0x40
			[Key("release_num_info")]
			public ReleaseNumInfo release_num_info; // 0x48
			[Key("team_stadium_user")]
			public TeamStadiumUser team_stadium_user; // 0x50
			[Key("follower_num")]
			public int follower_num; // 0x58
			[Key("own_follow_num")]
			public int own_follow_num; // 0x5C
			[Key("enable_circle_scout")]
			public int enable_circle_scout; // 0x60
		}
	}
	[MessagePackObject]
	public class UserFriend
    {
		[Key("friend_viewer_id")]
		public long friend_viewer_id; // 0x10
		[Key("state")]
		public int state; // 0x18
		[Key("follow_time")]
		public string follow_time; // 0x20
		[Key("follower_time")]
		public string follower_time; // 0x28
	}
	[MessagePackObject]
	public class DirectoryCard // TypeDefIndex: 7348
	{
		[Key("card_id")]
		public int card_id; // 0x10
		[Key("directory_ranking")]
		public int directory_ranking; // 0x14
		[Key("trained_chara")]
		public TrainedChara trained_chara; // 0x18
	}
	[MessagePackObject]
	public class UserSupportCard
	{
		[Key("viewer_id")]
		public long viewer_id; // 0x10
		[Key("support_card_id")]
		public int support_card_id; // 0x18
		[Key("exp")]
		public int exp; // 0x1C
		[Key("limit_break_count")]
		public int limit_break_count; // 0x20
		[Key("favorite_flag")]
		public int favorite_flag; // 0x24
		[Key("stock")]
		public int stock; // 0x28
		[Key("possess_time")]
		public string possess_time; // 0x30
	}
	[MessagePackObject]
	public class TrophyNumInfo
	{
		[Key("grade_1")]
		public int grade_1; // 0x10
		[Key("grade_2")]
		public int grade_2; // 0x14
		[Key("grade_3")]
		public int grade_3; // 0x18
		[Key("grade_ex")]
		public int grade_ex; // 0x1C
	}
	[MessagePackObject]
	public class ReleaseNumInfo
	{
		[Key("voice_num")]
		public int voice_num; // 0x10
		[Key("act_num")]
		public int act_num; // 0x14
		[Key("good_end_num")]
		public int good_end_num; // 0x18
		[Key("music_num")]
		public int music_num; // 0x1C
		[Key("main_story_num")]
		public int main_story_num; // 0x20
		[Key("chara_story_num")]
		public int chara_story_num; // 0x24
		[Key("card_num")]
		public int card_num; // 0x28
		[Key("support_card_num")]
		public int support_card_num; // 0x2C
		[Key("chara_event_num")]
		public int chara_event_num; // 0x30
		[Key("support_event_num")]
		public int support_event_num; // 0x34
		[Key("scenario_event_num")]
		public int scenario_event_num; // 0x38
	}
	[MessagePackObject]
	public class TeamStadiumUser
	{
		[Key("team_class")]
		public int team_class; // 0x10
		[Key("best_team_class")]
		public int best_team_class; // 0x14
		[Key("best_point")]
		public int best_point; // 0x18
	}
}
