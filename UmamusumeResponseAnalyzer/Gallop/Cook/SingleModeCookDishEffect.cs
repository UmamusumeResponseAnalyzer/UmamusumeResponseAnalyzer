using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallop
{
    // 原始ORM内容
    /*
    public class SingleModeCookDishEffect
    {
        public int Id;
        public int EffectGroupId;
        public int EffectType;
        public int EffectValue1;
        public int EffectValue2;
        public int EffectValue3;
        public int EffectValue4;
    }
    */
    public class SingleModeCookDishEffect
    {
        public int effect_type;
        public int effect_value_1;
        public int effect_value_2;
        public int effect_value_3;
        public int effect_value_4;
    }
}
