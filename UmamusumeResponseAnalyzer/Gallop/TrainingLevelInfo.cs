using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class TrainingLevelInfo
    {
        [Key("command_id")]
        public int command_id; // 0x10
        [Key("level")]
        public int level; // 0x14
    }
}
