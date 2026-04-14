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
        [Key("viewer_id")]
        public long viewer_id; // 0x10
        [Key("sid")]
        public string sid; // 0x18
        [Key("servertime")]
        public long servertime; // 0x20
        [Key("result_code")]
        public int result_code; // 0x28
    }
}
