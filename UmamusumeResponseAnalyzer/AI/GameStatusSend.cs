using Gallop;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using static UmamusumeResponseAnalyzer.Game.TurnInfo.TurnInfoUAF;

namespace UmamusumeResponseAnalyzer.AI
{
    public class GameStatusSend_UAF
    {
        public int umaId;//马娘编号，见KnownUmas.cpp
        public int umaStar;//几星
        public bool islegal;

        public int turn;//回合数，从0开始，到77结束
        public int vital;//体力，叫做“vital”是因为游戏里就这样叫的
        public int maxVital;//体力上限
        public int motivation;//干劲，从1到5分别是绝不调到绝好调

        public int[] fiveStatus;//五维属性，1200以上不减半
        public int[] fiveStatusLimit;//五维属性上限，1200以上不减半
        public int skillPt;//技能点
        public int skillScore;//已买技能的分数

        public double ptScoreRate;
        public int failureRateBias;//失败率改变量。练习上手=2，练习下手=-2

        public bool isAiJiao;//爱娇
        public bool isQieZhe;//切者
        public bool isPositiveThinking;//ポジティブ思考，友人第三段出行选上的buff，可以防一次掉心情

        public int[] zhongMaBlueCount;//种马的蓝因子个数，假设只有3星
        public int[] zhongMaExtraBonus;//种马的剧本因子以及技能白因子（等效成pt），每次继承加多少。全大师杯因子典型值大约是30速30力200pt

        public int saihou;
        public bool isRacing;
        public int[] cardId;
        public UAFPerson[] persons;//最多9个头。依次是6张卡，理事长6，记者7，没带卡的凉花8（带凉花卡了那就在前6个位置，8号位置就空下了）。
        public int[,] personDistribution;//每个训练有哪些人头id，personDistribution[哪个训练][第几个人头]，空人头为-1
        public int lockedTrainingId;

        //剧本相关
        public int[] uaf_trainingColor;//五种训练的颜色
        public int[] uaf_trainingLevel;//三种颜色五种训练的等级
        public bool[] uaf_winHistory;//运动会历史战绩

        public bool uaf_rankGainIncreased;//训练等级提升量是否+3
        public int uaf_xiangtanRemain;//还剩几次相谈

        public int[] uaf_buffActivated;//蓝红黄的buff已经触发过几次了？记录这个主要是用来识别什么时候应该增加两回合buff，比如假如训练后等级变成370，这时如果buffActivated=6则增加2回合buff并改成7（说明刚激活350级的buff），如果buffActivated=7则不增加buff（说明350级的buff已经激活过）
        public int[] uaf_buffNum;//蓝红黄的buff还剩几个？

        //单独处理凉花卡，因为接近必带。其他友人团队卡的以后再考虑
        public int lianghua_type;//0没带凉花卡，1 ssr卡，2 r卡
        public int lianghua_personId;//凉花卡在persons里的编号
                                     //int16_t lianghua_stage;//0是未点击，1是已点击但未解锁出行，2是已解锁出行    这次，凉花卡和其他友人的这个全放在Person类里了
        public int lianghua_outgoingStage;//0未点击，1点击还未解锁出行，2已解锁出行
        public int lianghua_outgoingUsed;//凉花的出行已经走了几段了   暂时不考虑其他友人团队卡的出行

        public GameStatusSend_UAF(Gallop.SingleModeCheckEventResponse @event)
        {

            if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0) || @event.data.race_start_info != null) return;
            bool uaf_liferace = true;
            for (int i = 0; i < 5; i++)
            {
                uaf_liferace &= (@event.data.home_info.command_info_array[i].is_enable==0);
            }

            if (uaf_liferace || (@event.data.chara_info.playing_state != 1))
            {
                islegal = false; //生涯比赛和UAF直接return，就不发了
                return;
            }
            islegal = true;
            //Console.WriteLine("测试用，看到这个说明发送成功\n");
            umaId = @event.data.chara_info.card_id;
            umaStar = @event.data.chara_info.rarity;
            //turn
            int turnNum = @event.data.chara_info.turn;//游戏里回合数从1开始
            turn = turnNum - 1;//ai里回合数从0开始
            vital = @event.data.chara_info.vital;
            maxVital = @event.data.chara_info.max_vital;
            motivation = @event.data.chara_info.motivation;

            fiveStatus = new int[]
            {
                ScoreUtils.ReviseOver1200(@event.data.chara_info.speed),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.stamina),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.power) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.guts) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.wiz) ,
            };

            fiveStatusLimit = new int[]
            {
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_speed),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_stamina),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_power) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_guts) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_wiz) ,
            };

            skillPt = @event.data.chara_info.skill_point;


            failureRateBias = 0;
            if (@event.data.chara_info.chara_effect_id_array.Contains(6))
            {
                failureRateBias = 2;
            }
            if (@event.data.chara_info.chara_effect_id_array.Contains(10))
            {
                failureRateBias = -2;
            }

            isAiJiao = @event.data.chara_info.chara_effect_id_array.Contains(8);
            isQieZhe = @event.data.chara_info.chara_effect_id_array.Contains(7);
            isPositiveThinking = @event.data.chara_info.chara_effect_id_array.Contains(25);
            ptScoreRate = 2.1;
            skillPt = 0;
            try
            {
                ptScoreRate = isQieZhe ? 2.3 : 2.1;
                double ptScore = AiUtils.calculateSkillScore(@event, ptScoreRate);
                skillPt = (int)(ptScore / ptScoreRate);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("获取当前技能分失败" + ex.Message);
            }

            skillScore = 0;

            cardId = new int[6];

            isPositiveThinking = @event.data.chara_info.chara_effect_id_array.Contains(25);

            bool LArcIsAbroad = (turnNum >= 37 && turnNum <= 43) || (turnNum >= 61 && turnNum <= 67);

            zhongMaBlueCount = new int[5];
            //用属性上限猜蓝因子个数
            {
                int[] defaultLimit = new int[5] { 2000, 2000, 1800, 1800, 1400 };
                double factor = 16;//每个三星因子可以提多少上限
                if (turn >= 54)//第二次继承结束
                    factor = 22;
                else if (turn >= 30)//第二次继承结束
                    factor = 19;
                for (int i = 0; i < 5; i++)
                {
                    int threeStarCount = (int)Math.Round((fiveStatusLimit[i] - defaultLimit[i]) / 2 / factor);
                    if (threeStarCount > 6) threeStarCount = 6;
                    if (threeStarCount < 0) threeStarCount = 0;
                    zhongMaBlueCount[i] = threeStarCount * 3;
                }
            }
            //从游戏json的id到ai的人头编号的换算

            foreach (var s in @event.data.chara_info.support_card_array)
            {
                int p = s.position - 1;
                //突破数+10*卡原来的id，例如神团是30137，满破神团就是301374
                cardId[p] = s.limit_break_count + s.support_card_id * 10;
            }

            lianghua_type = 0;
            persons = new UAFPerson[9];
            for (int i = 0; i < 9; i++)
                persons[i] = new UAFPerson();

            //var friendCards = new List<int>  //各种友人团队卡
            //{
            //    30160,
            //    10094
            //};
            lianghua_personId = 8;
            for (int i = 0; i < 6; i++)
            {
                if (cardId[i] / 10 == 30188)//ssr凉花
                {
                    lianghua_type = 1;
                    persons[i].cardID = cardId[i];
                    persons[i].isCard = true;
                    persons[i].personType = 1;

                    persons[i].friendship = @event.data.chara_info.evaluation_info_array[i].evaluation;
                    persons[i].cardRecord = 0;

                    lianghua_personId = i;

                }
                else if (cardId[i] / 10 == 10104)//r凉花
                {
                    lianghua_type = 2;
                    persons[i].cardID = cardId[i];
                    persons[i].isCard = true;
                    persons[i].personType = 1;

                    persons[i].friendship = @event.data.chara_info.evaluation_info_array[i].evaluation;
                    ;
                    lianghua_personId = i;
                }
                else if (cardId[i] / 10 == 30137)//神团
                {

                    persons[i].cardID = cardId[i];
                    persons[i].isCard = true;
                    persons[i].personType = 8;
                    //qingre 102
                    persons[i].friendship = @event.data.chara_info.evaluation_info_array[i].evaluation;


                }
                else if (cardId[i] / 10 == 30067)//卤豆腐
                {

                    persons[i].cardID = cardId[i];
                    persons[i].isCard = true;
                    persons[i].personType = 8;
                    //qingre 101
                    persons[i].friendship = @event.data.chara_info.evaluation_info_array[i].evaluation;
                }
                else
                {
                    persons[i].cardID = cardId[i];
                    persons[i].isCard = true;
                    persons[i].personType = 2;

                    persons[i].friendship = @event.data.chara_info.evaluation_info_array[i].evaluation;

                }

            }
            //理事长 记者 没带卡的凉花

            if(lianghua_personId==8)
            {
                persons[8].cardID = 0;
                persons[8].isCard = false;
                persons[8].personType = 6;

                foreach(var p in @event.data.chara_info.evaluation_info_array)
                {
                    if (p.target_id == 102)
                    {
                        //qiuchuan
                        persons[6].friendship = p.evaluation;
                    }
                    if (p.target_id == 103)
                    {
                        //yms
                        persons[7].friendship = p.evaluation;
                    }
                    if (p.target_id == 111)
                    {
                        //yms
                        persons[8].friendship = p.evaluation;
                    }
                }
            }
            else
            {
                persons[8].cardID = 0;
                persons[8].isCard = false;
                persons[8].personType = 0;
                foreach (var p in @event.data.chara_info.evaluation_info_array)
                {
                    if (p.target_id == 102)
                    {
                        //qiuchuan
                        persons[6].friendship = p.evaluation;
                    }
                    if (p.target_id == 103)
                    {
                        //yms
                        persons[7].friendship = p.evaluation;
                    }
                }
            }

            persons[6].personType = 4;
            persons[7].personType = 5;

            //到目前为止，headIdConvert写完了
            personDistribution = new int[5, 5];
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5; j++)
                    personDistribution[i, j] = -1;

            foreach (var train in @event.data.home_info.command_info_array)
            {
                //Console.WriteLine(train.command_id);
                if (!GameGlobal.ToTrainIndex.ContainsKey(train.command_id))//不是正常训练
                    continue;
                //Console.WriteLine("!");
                int trainId = GameGlobal.ToTrainIndex[train.command_id];

                int j = 0;
                foreach (var p in train.training_partner_array)
                {
                    personDistribution[trainId, j] = p == 102 ? 6 : p == 103 ? 7 : p == 111 ? 8 : p - 1;
                    j += 1;
                }
                foreach (var p in train.tips_event_partner_array)
                {

                    persons[p - 1].isHint = true;
                }
            }

            //计算Lockedtrainid
            bool istrainlocked = false;
            int enableidx = -1;
            var command = @event.data.home_info.command_info_array;
            foreach (var train in @event.data.home_info.command_info_array)
            {
                if (!GameGlobal.ToTrainIndex.ContainsKey(train.command_id))//不是正常训练
                    continue;
                if (train.is_enable != 1)
                {
                    istrainlocked = true;
                }
                else
                {
                    enableidx = Convert.ToInt32(train.command_id) % 10;
                }
            }

            if (istrainlocked)
            {
                lockedTrainingId = enableidx;
            }
            else
            {
                lockedTrainingId = -1;
            }

            //UAF剧本 蓝色1 红色2 黄色3
            var sportdat = @event.data.sport_data_set;
            uaf_trainingColor = new int[5];

            var commandsport = sportdat.command_info_array;

            for (int i = 0; i < 5; i++)
            {
                uaf_trainingColor[i] = (Convert.ToInt32(commandsport[i].command_id) % 1000) / 100;
            }

            uaf_trainingLevel = new int[15];
            for (int i = 0; i < 15; i++)
            {
                uaf_trainingLevel[i] = sportdat.training_array[i].sport_rank;
            }
            //winhistory
            uaf_winHistory = new bool[75]; //每15个是一次结果，分别是蓝红黄 五个属性顺序
            for (int i = 0; i < 75; i++)
            {
                uaf_winHistory[i] = false;
            }
            //int uafcount = 0;
            foreach (var result in sportdat.competition_result_array)
            {
                int uafcount = result.compe_type;
                //uafcount++;
                foreach (var item in result.win_command_id_array)
                {
                    int color = (Convert.ToInt32(item) % 1000) / 100;
                    int type = (Convert.ToInt32(item) % 10);
                    uaf_winHistory[(uafcount - 1) * 15 + (color - 1) * 5 + type - 1] = true;
                }
            }

            uaf_xiangtanRemain = sportdat.item_id_array.Count(x => x == 6);
            var turnInfoUAF = new TurnInfoUAF(@event.data);
            uaf_rankGainIncreased = turnInfoUAF.IsRankGainIncreased;

            uaf_buffActivated = new int[3];
            for(int i = 0; i < 3; i++)
            {
                int summ = 0;
                for (int j = 0; j < 5; j++)
                {
                    summ += sportdat.training_array[i * 5 + j].sport_rank;
                }
                uaf_buffActivated[i] = summ/50;
            }

            uaf_buffNum=new int[3];
            foreach(var p in sportdat.effected_stance_array)
            {
                uaf_buffNum[Convert.ToInt32(p.stance_id) / 100 - 1] = p.remain_count;
            }

            lianghua_outgoingStage = 0;
            //凉花出行用几次
            if (lianghua_personId != 8)
            {

                lianghua_outgoingUsed = @event.data.chara_info.evaluation_info_array[lianghua_personId].story_step;
                if (@event.data.chara_info.evaluation_info_array[lianghua_personId].is_outing == 1)
                    lianghua_outgoingStage = 2;
                else {
                    bool lianghuaClicked = false;//友人卡是否点过第一次
                    for (int t = GameStats.currentTurn; t >= 1; t--)
                    {
                        if (GameStats.stats[t] == null)
                        {
                            break;
                        }

                        if (!GameGlobal.TrainIds.Any(x => x == GameStats.stats[t].playerChoice)) //没训练
                            continue;
                        if (GameStats.stats[t].isTrainingFailed)//训练失败
                            continue;
                        if (!GameStats.stats[t].uaf_friendAtTrain[GameGlobal.ToTrainIndex[GameStats.stats[t].playerChoice]])
                            continue;//没点友人

                        lianghuaClicked = true;
                        break;
                    }
                    if (lianghuaClicked) lianghua_outgoingStage = 1;
                    else lianghua_outgoingStage = 0;
                }

            }
            else
            {
                lianghua_outgoingUsed = 0;
            }
        }
    }
}
