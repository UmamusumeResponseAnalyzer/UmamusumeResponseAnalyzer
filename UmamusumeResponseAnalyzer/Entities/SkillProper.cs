using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class SkillProper
    {
        public GroundType Ground { get; set; } = GroundType.None;
        public DistanceType Distance { get; set; } = DistanceType.None;
        public StyleType Style { get; set; } = StyleType.None;

        public enum GroundType
        {
            None,
            Turf,
            Dirt
        }
        public enum DistanceType
        {
            None,
            Short,
            Mile,
            Middle,
            Long
        }
        public enum StyleType
        {
            None,
            Nige,
            Senko,
            Sashi,
            Oikomi
        }
    }
}
