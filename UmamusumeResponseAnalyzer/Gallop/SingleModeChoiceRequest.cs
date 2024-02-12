using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SingleModeChoiceRequest
    {
        [Key("event_id")]
        public int event_id;
        
        [Key("chara_id")]
        public int chara_id;

        [Key("choice_number")]
        public int choice_number;

        [Key("current_turn")]
        public int current_turn;
    }
}
