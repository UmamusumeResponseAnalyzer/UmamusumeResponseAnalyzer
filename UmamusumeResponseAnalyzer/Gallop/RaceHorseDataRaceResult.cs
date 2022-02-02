using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class RaceHorseDataRaceResult
    {
        [Key("turn")]
        public int turn; // 0x10
        [Key("program_id")]
        public int program_id; // 0x14
        [Key("result_rank")]
        public int result_rank; // 0x18
    }
}
