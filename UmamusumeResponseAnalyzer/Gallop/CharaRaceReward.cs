using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class CharaRaceReward
    {
        [Key("result_rank")]
        public int result_rank; // 0x10
        [Key("result_time")]
        public int result_time; // 0x14
        [Key("race_reward")]
        public RaceRewardData[] race_reward; // 0x18
        [Key("race_reward_bonus")]
        public RaceRewardData[] race_reward_bonus; // 0x20
        [Key("race_reward_plus_bonus")]
        public RaceRewardData[] race_reward_plus_bonus; // 0x28
        [Key("race_reward_bonus_win")]
        public RaceRewardData[] race_reward_bonus_win; // 0x30
        [Key("gained_fans")]
        public int gained_fans; // 0x38
        [Key("campaign_id_array")]
        public int[] campaign_id_array; // 0x40
    }
    [MessagePackObject]
    public class RaceRewardData
    {
        [Key("item_type")]
        public int item_type; // 0x10
        [Key("item_id")]
        public int item_id; // 0x14
        [Key("item_num")]
        public int item_num; // 0x18
    }
}
