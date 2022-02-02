using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SuccessionEffectedFactor
    {
        [Key("position")]
        public int position; // 0x10
        [Key("factor_id_array")]
        public int[] factor_id_array; // 0x18
        [Key("factor_info_array")]
        public FactorInfo[] factor_info_array; // 0x20
    }
}
