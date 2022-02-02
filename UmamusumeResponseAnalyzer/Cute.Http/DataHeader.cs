using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cute.Http
{
    [MessagePackObject]
    public class DataHeader
    {
        [Key("result_code")]
        public int result_code; // 0x10
    }
}
