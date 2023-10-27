using Gallop;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.AI
{
    public class LArcPerson
    {
        public int personType;//0代表未加载（例如前两个回合的npc），1代表佐岳支援卡（R或SSR都行），2代表普通支援卡，3代表npc人头，4理事长，5记者，6不带卡的佐岳。暂不支持其他友人/团队卡
        //int16_t cardId;//支援卡id，不是支援卡就0
        public int charaId;//npc人头的马娘id，不是npc就0，懒得写也可以一律0（只用于获得npc的名字）

        public int cardIdInGame;// Game.cardParam里的支援卡序号，非支援卡为-1
        public int friendship;//羁绊
                              //bool atTrain[5];//是否在五个训练里。对于普通的卡只是one-hot或者全空，对于ssr佐岳可能有两个true
                              //bool isShining;//是否闪彩。无法闪彩的卡或者npc恒为false
        public bool isHint;//是否有hint。友人卡或者npc恒为false
        public int cardRecord;//记录一些可能随着时间而改变的参数，例如根涡轮的固有

        //bool larc_isLinkCard;//是否为link支援卡
        public int larc_charge;//现在充了几格
        public int larc_statusType;//速耐力根智01234
        public int larc_specialBuff;//每3级的特殊固有，编号同游戏内
        public int larc_level;//几级
        public int larc_buffLevel;//第几个buff
        public int[] larc_nextThreeBuffs;//当前以及以下两级的buff

        //isShining, larc_isLinkCard, distribution 在ai里计算


        public LArcPerson() {
            personType = 0;
            charaId = 0;
            cardIdInGame = -1;
            friendship = 0;
            isHint = false;
            cardRecord = 0;
            larc_charge = 0;
            larc_statusType = -1;
            larc_specialBuff = 0;
            larc_level = 0;
            larc_buffLevel = 0;
            larc_nextThreeBuffs = new int[3] { 0, 0, 0 };
        }
    }
}
