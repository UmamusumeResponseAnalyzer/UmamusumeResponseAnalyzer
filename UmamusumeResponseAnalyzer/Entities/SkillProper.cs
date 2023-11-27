using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class SkillProper
    {
        /// <summary>
        /// 场地适性
        /// </summary>
        public GroundType Ground { get; set; } = GroundType.None;
        /// <summary>
        /// 距离适性
        /// </summary>
        public DistanceType Distance { get; set; } = DistanceType.None;
        /// <summary>
        /// 跑法适性
        /// </summary>
        public StyleType Style { get; set; } = StyleType.None;

        public enum GroundType
        {
            None,
            /// <summary>
            /// 芝
            /// </summary>
            Turf,
            /// <summary>
            /// 泥
            /// </summary>
            Dirt
        }
        public enum DistanceType
        {
            None,
            /// <summary>
            /// 短
            /// </summary>
            Short,
            /// <summary>
            /// 英
            /// </summary>
            Mile,
            /// <summary>
            /// 中
            /// </summary>
            Middle,
            /// <summary>
            /// 长
            /// </summary>
            Long
        }
        public enum StyleType
        {
            None,
            /// <summary>
            /// 逃
            /// </summary>
            Nige,
            /// <summary>
            /// 先
            /// </summary>
            Senko,
            /// <summary>
            /// 差
            /// </summary>
            Sashi,
            /// <summary>
            /// 追
            /// </summary>
            Oikomi
        }
    }
}
