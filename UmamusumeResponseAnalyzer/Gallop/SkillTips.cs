using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SkillTips
    {
        [Key("group_id")]
        public int group_id; // 0x10
        [Key("rarity")]
        public int rarity; // 0x14
        [Key("level")]
        public int level; // 0x18
    }
}
