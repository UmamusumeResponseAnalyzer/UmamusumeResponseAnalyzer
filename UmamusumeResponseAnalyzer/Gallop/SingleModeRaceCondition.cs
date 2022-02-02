using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SingleModeRaceCondition
    {
        [Key("program_id")]
        public int program_id; // 0x10
        [Key("weather")]
        public int weather; // 0x14
        [Key("ground_condition")]
        public int ground_condition; // 0x18
    }
}
