using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    [MessagePackObject]
    public class SkillUpgradeInfo
    {
        [Key("condition_id")]
        public int condition_id;
        [Key("total_count")]
        public int total_count;
        [Key("current_count")]
        public int current_count;
    }
}
