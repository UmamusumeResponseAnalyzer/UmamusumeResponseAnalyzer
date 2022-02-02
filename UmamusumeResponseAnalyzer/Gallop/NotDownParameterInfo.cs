using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class NotDownParameterInfo
    {
        [Key("evaluation_chara_id_array")]
        public int[] evaluation_chara_id_array; // 0x10
    }
}
