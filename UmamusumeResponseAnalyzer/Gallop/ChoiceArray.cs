using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class ChoiceArray
    {
        [Key("select_index")]
        public int select_index; // 0x10
        [Key("receive_item_id")]
        public int receive_item_id; // 0x14
        [Key("target_race_id")]
        public int target_race_id; // 0x18
    }
}
