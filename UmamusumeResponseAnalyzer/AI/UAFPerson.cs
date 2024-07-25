using System;

namespace UmamusumeResponseAnalyzer.AI
{
    public class UAFPerson
    {
        public bool isCard;//是否为支援卡，否则为理事长记者或者不带卡的凉花
        public int cardID;//支援卡ID
        public int personType;//0代表未知，1代表凉花支援卡（R或SSR都行），2代表普通支援卡，4理事长，5记者，6不带卡的凉花，7其他友人卡，8其他团队卡。
        public int friendship;//羁绊
        public bool isHint;//是否有hint。友人卡或者npc恒为false
        public int cardRecord;//记录一些可能随着时间而改变的参数，例如根涡轮的固有
        public int friendOrGroupCardStage;//只对友人卡团队卡有效，0是未点击，1是已点击但未解锁出行，2是已解锁出行但没情热，3是情热状态
        public int groupCardShiningContinuousTurns;//团队卡情热了几个回合了（下回合结束情热的概率与此有关，数据可以在大师杯版ai里找到）

        //isShining, larc_isLinkCard, distribution 在ai里计算
        public UAFPerson() {
            isCard = false;
            personType = 0;
            friendship = 0;
            isHint = false;
            cardRecord = 0;
            friendOrGroupCardStage = 0;
            groupCardShiningContinuousTurns = 0;
        }
    }
}
