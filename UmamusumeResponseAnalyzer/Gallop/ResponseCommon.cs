using Cute.Http;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class ResponseCommon
    {
        [Key("data_headers")]
        public DataHeader data_headers;
    }
}
