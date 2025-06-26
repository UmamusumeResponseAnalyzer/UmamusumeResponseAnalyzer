using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SingleModeSportDataSet
    {
        [Key("training_array")]
        public SingleModeSportTraining[] training_array;
        [Key("command_info_array")]
        public SingleModeSportCommandInfo[] command_info_array;
        [Key("item_id_array")]
        public int[] item_id_array;
        [Key("effected_item_id_array")]
        public int[] effected_item_id_array;
        [Key("competition_result_array")]
        public SingleModeSportCompetitionResult[] competition_result_array;
        [Key("effected_stance_array")]
        public SingleModeSportEffectedStance[] effected_stance_array;
        [Key("compe_effect_id_array")]
        public int[] compe_effect_id_array;
    }
    [MessagePackObject]
    public class SingleModeSportTraining
    {
        [Key("command_type")]
        public int command_type;
        [Key("command_id")]
        public int command_id;
        [Key("sport_rank")]
        public int sport_rank;
    }
    [MessagePackObject]
    public class SingleModeSportCommandInfo
    {
        [Key("command_type")]
        public int command_type;
        [Key("command_id")]
        public int command_id;
        [Key("params_inc_dec_info_array")]
        public SingleModeParamsIncDecInfo[] params_inc_dec_info_array;
        [Key("gain_sport_rank_array")]
        public SingleModeSportGainRank[] gain_sport_rank_array;

        [MessagePackObject]
        public class SingleModeSportGainRank
        {
            [Key("command_id")]
            public int command_id;
            [Key("gain_rank")]
            public int gain_rank;
        }
    }
    [MessagePackObject]
    public class SingleModeSportCompetitionResult
    {
        [Key("compe_type")]
        public int compe_type;
        [Key("result_state")]
        public int result_state;
        [Key("win_command_id_array")]
        public int[] win_command_id_array;
        [Key("prize_command_id_array")]
        public int[] prize_command_id_array;
    }
    [MessagePackObject]
    public class SingleModeSportEffectedStance
    {
        [Key("stance_id")]
        public int stance_id;
        [Key("remain_count")]
        public int remain_count;
    }
}
