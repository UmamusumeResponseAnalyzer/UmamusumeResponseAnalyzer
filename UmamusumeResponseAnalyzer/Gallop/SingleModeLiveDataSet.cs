using Gallop;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SingleModeLiveDataSet
    {
        [Key("command_info_array")]
        public SingleModeLiveCommandInfo[] command_info_array; // 0x20
    }
    [MessagePackObject]
    public class SingleModeLiveCommandInfo
    {
        [Key("command_type")]
        public int command_type; // 0x10
        [Key("command_id")]
        public int command_id; // 0x14
        [Key("params_inc_dec_info_array")]
        public SingleModeParamsIncDecInfo[] params_inc_dec_info_array; // 0x18
    }
}
