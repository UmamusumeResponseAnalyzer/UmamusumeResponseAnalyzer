using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class PracticeRaceRaceStartResponse
    {
        [Key("data")]
        public CommonResponse data; // 0x18

        [MessagePackObject]
        public class CommonResponse // TypeDefIndex: 6740
        {
            [Key("trained_chara_array")]
            public TrainedChara[] trained_chara_array; // 0x10
            [Key("race_result_info")]
            public RaceResultInfo race_result_info; // 0x18
            [Key("entry_info_array")]
            public PracticeRaceEntryInfo[] entry_info_array; // 0x20
            [Key("practice_race_id")]
            public int practice_race_id; // 0x28
            [Key("state")]
            public int state; // 0x2C
            [Key("practice_partner_owner_info_array")]
            public PracticePartnerOwnerInfo[] practice_partner_owner_info_array; // 0x30
        }
    }
    [MessagePackObject]
    public class RaceResultInfo // TypeDefIndex: 7434
    {
        [Key("race_instance_id")]
        public int race_instance_id; // 0x10
        [Key("race_horse_data_array")]
        public RaceHorseData[] race_horse_data_array; // 0x18
        [Key("season")]
        public int season; // 0x20
        [Key("weather")]
        public int weather; // 0x24
        [Key("ground_condition")]
        public int ground_condition; // 0x28
        [Key("random_seed")]
        public int random_seed; // 0x2C
        [Key("race_scenario")]
        public string race_scenario; // 0x30
    }
    [MessagePackObject]
    public class PracticeRaceEntryInfo // TypeDefIndex: 7423
    {
        [Key("entry_id")]
        public int entry_id; // 0x10
        [Key("frame_order")]
        public int frame_order; // 0x14
    }
    [MessagePackObject]
    public class PracticePartnerOwnerInfo // TypeDefIndex: 7420
    {
        [Key("partner_trained_chara_id")]
        public int partner_trained_chara_id; // 0x10
        [Key("owner_viewer_id")]
        public long owner_viewer_id; // 0x18
        [Key("owner_name")]
        public string owner_name; // 0x20
        [Key("owner_trained_chara_id")]
        public int owner_trained_chara_id; // 0x28
        [Key("friend_state")]
        public int friend_state; // 0x2C
    }
}
