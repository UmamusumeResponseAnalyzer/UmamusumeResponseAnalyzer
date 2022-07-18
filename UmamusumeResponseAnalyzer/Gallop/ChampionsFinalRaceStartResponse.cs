using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class ChampionsFinalRaceStartResponse
    {
        [Key("data")]
        public CommonResponse data; // 0x18

        [MessagePackObject]
        public class CommonResponse // TypeDefIndex: 6740
        {
            [Key("room_info")]
            public ChampionsRoomInfo room_info;
            [Key("room_user_array")]
            public ChampionsRoomUser[] room_user_array;
            [Key("race_horse_data_array")]
            public RaceHorseData[] race_horse_data_array;
            [Key("trained_chara_array")]
            public TrainedChara[] trained_chara_array;
            [Key("state")]
            public int state;
        }
    }
    [MessagePackObject]
    public class ChampionsRoomInfo
    {
        [Key("room_id")]
        public long room_id;
        [Key("user_entry_num")]
        public int user_entry_num;
        [Key("race_start_time")]
        public string race_start_time;
        [Key("race_instance_id")]
        public int race_instance_id;
        [Key("season")]
        public int season;
        [Key("weather")]
        public int weather;
        [Key("ground_condition")]
        public int ground_condition;
        [Key("random_seed")]
        public int random_seed;
        [Key("race_scenario")]
        public string race_scenario;
    }
    [MessagePackObject]
    public class ChampionsRoomUser
    {
        [Key("room_id")]
        public long room_id;
        [Key("viewer_id")]
        public long viewer_id;
        [Key("name")]
        public string name;
        [Key("honor_id")]
        public int honor_id;
        [Key("team_id")]
        public int team_id;
        [Key("entry_chara_array")]
        public ChampionsUserChara[] entry_chara_array;
    }
    [MessagePackObject]
    public class ChampionsUserChara
    {
        [Key("chara_id")]
        public int chara_id;
        [Key("race_cloth_id")]
        public int race_cloth_id;
        [Key("nick_name_id")]
        public int nick_name_id;
        [Key("team_member_id")]
        public int team_member_id;
    }
}
