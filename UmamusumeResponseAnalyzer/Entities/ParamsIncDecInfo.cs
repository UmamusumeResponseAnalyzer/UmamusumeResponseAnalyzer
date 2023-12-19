using Gallop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class ParamsIncDecInfo : Dictionary<int, int>
    {
        public ParamsIncDecInfo(Dictionary<int, int> dic) : base(dic) { }
        public void Add(SingleModeParamsIncDecInfo b) => this[b.target_type] += b.value;
        public void Add(SingleModeParamsIncDecInfo[] b)
        {
            foreach (var i in b)
            {
                this[i.target_type] += i.value;
            }
        }
    }
}
