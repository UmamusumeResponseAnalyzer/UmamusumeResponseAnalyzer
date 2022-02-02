using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SingleModeSuccessionEventInfo
    {
        [Key("effect_type")]
        public int effect_type; // 0x10
    }
}
