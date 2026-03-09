using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using static UmamusumeResponseAnalyzer.Localization.Game;

namespace Gallop
{
    [MessagePackObject]
    public class SingleModeSuccessionEventInfo
    {
        [Key("effect_type")]
        public int effect_type; // 0x10
        [Key("succession_gain_info_array")]
        public SingleModeSuccessionGainInfo[] succession_gain_info_array;
    }

    /// <summary>
    /// 五周年新增的继承选择信息
    /// </summary>
    [MessagePackObject]
    public class SingleModeSuccessionGainInfo
    {
        [Key("lottery_id")]
        public int lottery_id;
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
        [Key("skill_point")]
        public int skill_point; // 0xD4
        [Key("skill_tips_array")]
        public SkillTips[] skill_tips_array;
        [Key("effected_factor_array")]
        public SuccessionEffectedFactor[] effected_factor_array;

        [IgnoreMember]
        public int[] FiveStatus => new int[] { speed, stamina, power, guts, wiz };
        [IgnoreMember]
        public Dictionary<string, int> Proper => new Dictionary<string, int>
        {
            { I18N_Short, proper_distance_short },
            { I18N_Mile, proper_distance_mile },
            { I18N_Middle, proper_distance_middle },
            { I18N_Long, proper_distance_long },
            { I18N_Nige, proper_running_style_nige },
            { I18N_Oikomi, proper_running_style_oikomi },
            { I18N_Sashi, proper_running_style_sashi },
            { I18N_Senko, proper_running_style_senko },
            { I18N_Grass, proper_ground_turf },
            { I18N_Dirt, proper_ground_dirt }
        };
    }
}
