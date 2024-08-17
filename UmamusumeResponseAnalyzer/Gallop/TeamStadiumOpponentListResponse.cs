using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class TeamStadiumOpponentListResponse
    {
		[Key("data")]
        public CommonResponse data; // 0x18

		[MessagePackObject]
        public class CommonResponse // TypeDefIndex: 7152
        {
			[Key("opponent_info_array")]
			public TeamStadiumOpponent[] opponent_info_array; // 0x10
            [Key("opponent_info_copy")]
            public TeamStadiumOpponent? opponent_info_copy; // 0x10
        }
	}
	[MessagePackObject]
	public class TeamStadiumOpponent // TypeDefIndex: 7535
	{
		[Key("strength")]
		public int strength; // 0x10
		[Key("opponent_viewer_id")]
		public long opponent_viewer_id; // 0x18
		[Key("evaluation_point")]
		public int evaluation_point; // 0x20
		[Key("user_info")]
		public UserInfoAtFriend user_info; // 0x28
		[Key("team_data_array")]
		public TeamStadiumTeamData[] team_data_array; // 0x30
		[Key("trained_chara_array")]
		public TrainedChara[] trained_chara_array; // 0x38
		[Key("winning_reward_guarantee_status")]
		public int winning_reward_guarantee_status; // 0x40
	}
	[MessagePackObject]
	public class TeamStadiumTeamData // TypeDefIndex: 7548
	{
		[Key("distance_type")]
		public int distance_type; // 0x10
		[Key("member_id")]
		public int member_id; // 0x14
		[Key("trained_chara_id")]
		public int trained_chara_id; // 0x18
		[Key("running_style")]
		public int running_style; // 0x1C
	}
}
