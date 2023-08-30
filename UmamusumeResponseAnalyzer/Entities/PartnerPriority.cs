using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    //优先级。数字越小越优先，不能超过9
    //最后直接放在字符串最前面，然后排序，然后删掉第一位
    //0友人
    //1闪彩的卡
    //2没闪彩的但需要拉羁绊的卡
    //3其他卡
    //4需要充电的乱七八糟人头
    //5理事长记者
    //6无用人头
    public enum PartnerPriority
    {
        友人 = 0,
        闪 = 1,
        羁绊不足 = 2,
        其他 = 3,
        需要充电 = 4, //LArc独占？
        关键NPC = 5,
        无用NPC = 6,
        默认 = 7
    }
}
