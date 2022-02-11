using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class RoomMatchRaceStartResponse
    {
        [Key("data")]
        public CommonResponse data; // 0x18

        [MessagePackObject]
        public class CommonResponse // TypeDefIndex: 6884
        {
            [Key("race_scenario")]
            public string race_scenario; // 0x10
            [Key("random_seed")]
            public int random_seed; // 0x18
            [Key("race_horse_data_array")]
            public RaceHorseData[] race_horse_data_array; // 0x20
            [Key("trained_chara_array")]
            public TrainedChara[] trained_chara_array; // 0x28
            [Key("season")]
            public int season; // 0x30
            [Key("weather")]
            public int weather; // 0x34
            [Key("ground_condition")]
            public int ground_condition; // 0x38
        }
    }
}
