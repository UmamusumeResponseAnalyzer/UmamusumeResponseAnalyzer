using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class SuccessionRelationTable
    {
        public Dictionary<int, int> PointDictionary { get; set; }
        public Dictionary<int, List<int>> MemberDictionary { get; set; }
    }
}
