using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class FactorInfo
    {
        [Key("factor_id")]
        public int factor_id; // 0x10
        [Key("level")]
        public int level; // 0x14
    }
}
