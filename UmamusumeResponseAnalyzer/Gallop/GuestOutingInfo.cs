using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class GuestOutingInfo
    {
        [Key("support_card_id")]
        public int support_card_id; // 0x10
        [Key("story_step")]
        public int story_step; // 0x14
    }
}
