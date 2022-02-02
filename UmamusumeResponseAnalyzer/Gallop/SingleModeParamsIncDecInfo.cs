using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SingleModeParamsIncDecInfo
    {
        [Key("target_type")]
        public int target_type; // 0x10
        [Key("value")]
        public int value; // 0x14
    }
}
