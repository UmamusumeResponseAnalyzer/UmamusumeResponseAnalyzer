using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public  class RaceAnalyzeRequest
    {
        [Key("program_id")]
        public int program_id; // 0x70
        [Key("current_turn")]
        public int current_turn; // 0x74
    }
}
