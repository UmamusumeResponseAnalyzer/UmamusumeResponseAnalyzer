using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class MinigameResult
    {
        [Key("result_state")]
        public int result_state; // 0x10
        [Key("result_value")]
        public int result_value; // 0x14
        [Key("result_detail_array")]
        public MinigameResultDetail[] result_detail_array; // 0x18
    }
}
