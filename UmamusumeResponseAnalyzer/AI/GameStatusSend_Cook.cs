using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using UmamusumeResponseAnalyzer.Game;
using Spectre.Console;

namespace UmamusumeResponseAnalyzer.AI
{
    public class CookPerson
    {
        public int personType;//0代表未加载（例如前两个回合的npc），1代表友人（R或SSR都行），2代表普通支援卡，3代表npc人头，4理事长，5记者，6不带卡的佐岳。暂不支持其他友人/团队卡
                                //int16_t cardId;//支援卡id，不是支援卡就0
        public int charaId;//npc人头的马娘id，不是npc就0，懒得写也可以一律0（只用于获得npc的名字）
        public int friendship;//羁绊
                                //bool atTrain[5];//是否在五个训练里。对于普通的卡只是one-hot或者全空，对于ssr佐岳可能有两个true
                                //bool isShining;//是否闪彩。无法闪彩的卡或者npc恒为false
        public bool isHint;//是否有hint。友人卡或者npc恒为false
        public int cardRecord;//记录一些可能随着时间而改变的参数，例如根涡轮的固有

        public CookPerson()
        {
            personType = 0;
            charaId = 0;
            friendship = 0;
            isHint = false;
            cardRecord = 0;
        }        
    }
    public class GameStatusSend_Cook
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
        public int[] trainLevelCount;

        public double ptScoreRate;
        public int failureRateBias;//失败率改变量。练习上手=2，练习下手=-2

        public bool isQieZhe;//切者
        public bool isAiJiao;//爱娇
        public bool isPositiveThinking;//ポジティブ思考，友人第三段出行选上的buff，可以防一次掉心情
        public bool isRefreshMind;//休息的心得,每回合体力+5

        public int[] zhongMaBlueCount;//种马的蓝因子个数，假设只有3星
        public int[] zhongMaExtraBonus;//种马的剧本因子以及技能白因子（等效成pt），每次继承加多少。全大师杯因子典型值大约是30速30力200pt

        public int saihou;
        public bool isRacing;
        public int[] cardId;
        public CookPerson[] persons;//依次是6张卡
        public int[,] personDistribution;//每个训练有哪些人头id，personDistribution[哪个训练][第几个人头]，空人头为-1
        public int lockedTrainingId;
        public int friendship_noncard_yayoi;//非卡理事长的羁绊，带了理事长卡就是0
        public int friendship_noncard_reporter;//非卡记者的羁绊

        //剧本相关
        public int[] cook_material;//
        public int cook_dish_pt;//
        public int cook_dish;//
        public int[] cook_farm_level;
        public int cook_farm_pt;//
        public bool cook_dish_sure_success;
        public int[] cook_win_history;
        public int[] cook_harvest_history;
        public bool[] cook_harvest_green_history;
        public int[] cook_harvest_num;//当前收菜数量。UmaAI里没有同名变量，会在UmaAI端处理成需要的形式

        public int[] cook_train_material_type;//
        public bool[] cook_train_green;//
        public int cook_main_race_material_type;//

        //单独处理友人卡，因为接近必带。其他友人团队卡的以后再考虑
        public int friend_type;//0没带友人卡，1 ssr卡，2 r卡
        public int friend_personId;//友人卡在persons里的编号
        public int friend_stage;//0未点击，1点击还未解锁出行，2已解锁出行
        public int friend_outgoingUsed;//出行已经走了几段了   暂时不考虑其他友人团队卡的出行

        public GameStatusSend_Cook(Gallop.SingleModeCheckEventResponse @event)
        {
            islegal = false;
            if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0)) return;
            if ((@event.data.chara_info.playing_state != 1))
            {
                //重复显示的回合直接return，就不发了
                return;
            }

            //if(@event.data.race_start_info != null)
            isRacing = true;
            for (var i = 0; i < 5; i++)
            {
                isRacing &= (@event.data.home_info.command_info_array[i].is_enable == 0);
            }
            
            islegal = true;
            //Console.WriteLine("测试用，看到这个说明发送成功\n");
            umaId = @event.data.chara_info.card_id;
            umaStar = @event.data.chara_info.rarity;
            //turn
            var turnNum = @event.data.chara_info.turn;//游戏里回合数从1开始
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


            failureRateBias = 0;
            foreach (var effect in @event.data.chara_info.chara_effect_id_array)
            {
                switch (effect)
                {
                    case 6:
                        failureRateBias = 2; break;
                    case 10:
                        failureRateBias = -2; break;
                    case 7:
                        isQieZhe = true; break;
                    case 8:
                        isAiJiao = true; break;
                    case 25:
                        isPositiveThinking = true; break;
                    case 32:
                        isRefreshMind = true; break;
                }
            }

            ptScoreRate = 2.0; 
            skillPt = 0;
            try
            {
                ptScoreRate = isQieZhe ? 2.2 : 2.0;
                double ptScore = AiUtils.calculateSkillScore(@event, ptScoreRate);
                skillPt = (int)(ptScore / ptScoreRate);
                ptScoreRate = 2.0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("获取当前技能分失败" + ex.Message);
                skillPt = @event.data.chara_info.skill_point;
            }

            skillScore = 0;
            cardId = new int[6];

            zhongMaBlueCount = new int[5];
            //用属性上限猜蓝因子个数
            {
                var defaultLimit = GameGlobal.FiveStatusLimit[@event.data.chara_info.scenario_id];
                double factor = 16;//每个三星因子可以提多少上限
                if (turn >= 54)//第二次继承结束
                    factor = 22;
                else if (turn >= 30)//第二次继承结束
                    factor = 19;
                for (var i = 0; i < 5; i++)
                {
                    var div = (defaultLimit[i] >= 1200 ? 2 : 1);
                    var threeStarCount = (int)Math.Round((fiveStatusLimit[i] - defaultLimit[i]) / div / factor);
                    if (threeStarCount > 6) threeStarCount = 6;
                    if (threeStarCount < 0) threeStarCount = 0;
                    zhongMaBlueCount[i] = threeStarCount * 3;
                }
            }


            trainLevelCount = new int[5] { 0, 0, 0, 0, 0 };
            var trainClickCount = new int[5] { 0, 0, 0, 0, 0 };

            for (int t = @event.data.chara_info.turn - 1; t >= 1; t--)
            {
                if (GameStats.stats[t] == null)
                {
                    break;
                }

                if (!GameGlobal.TrainIds.Any(x => x == GameStats.stats[t].playerChoice)) //没训练
                    continue;
                if (GameStats.stats[t].isTrainingFailed)//训练失败
                    continue;
                if ((t >= 37 && t <= 40) || (t >= 61 && t <= 64)) //合宿点的次数不算
                    continue;
                int trainIdx = GameGlobal.ToTrainIndex[GameStats.stats[t].playerChoice];
                trainClickCount[trainIdx] += 1;
            }

            for (var i = 0; i < 5; i++)
            {
                int trLevel = @event.data.chara_info.training_level_info_array.First(x => x.command_id == GameGlobal.TrainIds[i]).level;
                int count = (trLevel - 1) * 4;
                if (count < 16)
                    count += trainClickCount[i] % 4;
                trainLevelCount[i] = count;
            }



            //从游戏json的id到ai的人头编号的换算
            foreach (var s in @event.data.chara_info.support_card_array)
            {
                var p = s.position - 1;
                //突破数+10*卡原来的id，例如神团是30137，满破神团就是301374
                cardId[p] = s.limit_break_count + s.support_card_id * 10;
            }

            persons = new CookPerson[6];
            for (int i = 0; i < 6; i++)
                persons[i] = new CookPerson();

            friend_type = 0;
            friend_personId = 0;
            for (int i = 0; i < 6; i++)
            {
                var personJson = @event.data.chara_info.evaluation_info_array.First(x => x.target_id == i + 1);
                persons[i].cardRecord = 0;
                persons[i].friendship = personJson.evaluation;
                switch (cardId[i] / 10)
                {
                    case 30207://ssr 理事长
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 1;
                        break;
                    case 10109://r 理事长
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 2;
                        break;
                    default:
                        persons[i].personType = 2;
                        break;
                }
            }
            friendship_noncard_yayoi = @event.data.chara_info.evaluation_info_array.Any(x => x.target_id == 102) ?
                @event.data.chara_info.evaluation_info_array.First(x => x.target_id == 102).evaluation : 0;
            friendship_noncard_reporter = @event.data.chara_info.evaluation_info_array.Any(x => x.target_id == 103)?
                @event.data.chara_info.evaluation_info_array.First(x => x.target_id == 103).evaluation : 0;
            
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
                    int personIdUmaAi = p == 102 ? 6 : p == 103 ? 7 : p >= 1000 ? 8 : p - 1;
                    personDistribution[trainId, j] = personIdUmaAi;
                    j += 1;
                }
                foreach (var p in train.tips_event_partner_array)
                {

                    persons[p - 1].isHint = true;
                }
            }

            //计算Lockedtrainid
            {
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
            }

            cook_material = new int[5];
            for (int i = 0; i < 5; i++)
            {
                cook_material[i] = @event.data.cook_data_set.material_info_array.First(x => x.material_id == 100 * (i + 1)).num;
            }
            cook_dish_pt = @event.data.cook_data_set.cook_info.cooking_friends_power;
            cook_dish = @event.data.cook_data_set.dish_info == null ? 0 :
                GameGlobal.CookDishIdUmaAI[@event.data.cook_data_set.dish_info.dish_id];

            cook_farm_level = new int[5];
            for (int i = 0; i < 5; i++)
            {
                cook_farm_level[i] = @event.data.cook_data_set.facility_info_array.First(x => x.facility_id == 100 * (i + 1)).facility_level;
            }
            cook_farm_pt = @event.data.cook_data_set.cook_info.care_point;
            cook_dish_sure_success = @event.data.cook_data_set.cooking_success_rate == 100;


            cook_win_history = new int[5] { 0, 0, 0, 0, 0 };
            foreach(var i in @event.data.cook_data_set.cook_result_info_array)
            {
                cook_win_history[i.cook_type - 1] = i.result_state - 1;
            }


            cook_harvest_history = new int[4] { -1, -1, -1, -1 };
            cook_harvest_green_history = new bool[4] { false, false, false, false};
            foreach (var i in @event.data.cook_data_set.care_history_info_array)
            {
                int tr = i.material_id / 100 - 1;
                int idx = i.num - 1;
                bool isGreen= i.boost_type == 6 || i.boost_type == 4 || i.boost_type == 2;
                cook_harvest_history[idx] = tr;
                cook_harvest_green_history[idx] = isGreen;
            }

            cook_harvest_num = new int[5];
            for (int i = 0; i < 5; i++)
            {
                cook_harvest_num[i] = @event.data.cook_data_set.material_harvest_info_array.First(x => x.material_id == 100 * (i + 1)).harvest_num;
            }

            cook_train_material_type = new int[8] { -1, -1, -1, -1, -1, -1, -1, -1 };
            cook_train_green = new bool[8] { false, false, false, false, false, false, false, false };

            foreach (var i in @event.data.cook_data_set.command_material_care_info_array)
            {
                bool isGreen = false;
                if (i.boost_type == 6 || i.boost_type == 4 || i.boost_type == 2)
                    isGreen = true;
                else if (!(i.boost_type == 5 || i.boost_type == 3 || i.boost_type == 0))
                    throw new Exception("unknown boost_type");
                int matType = i.material_id / 100 - 1;

                int training = i.command_type == 1 ? GameGlobal.ToTrainIndex[i.command_id] :
                    i.command_type == 7 ? 5 :
                    i.command_type == 3 ? 6 :
                    i.command_type == 4 ? 7 : -1;
                if (training >= 0)
                {
                    cook_train_material_type[training] = matType;
                    cook_train_green[training] = isGreen;
                }
            }
            cook_main_race_material_type = cook_train_material_type[7];

            friend_stage = 0;
            //友人出行用了几次
            if (friend_type != 0)
            {
                var friendJson = @event.data.chara_info.evaluation_info_array.First(x => x.target_id == friend_personId + 1);
                friend_outgoingUsed = friendJson.story_step;
                if (friendJson.is_outing == 1)
                    friend_stage = 2;
                else
                {
                    bool friendClicked = false;//友人卡是否点过第一次
                    for (int t = @event.data.chara_info.turn - 1; t >= 1; t--)
                    {
                        if (GameStats.stats[t] == null)
                        {
                            break;
                        }

                        if (!GameGlobal.TrainIds.Any(x => x == GameStats.stats[t].playerChoice)) //没训练
                            continue;
                        if (GameStats.stats[t].isTrainingFailed)//训练失败
                            continue;
                        if (!GameStats.stats[t].cook_friendAtTrain[GameGlobal.ToTrainIndex[GameStats.stats[t].playerChoice]])
                            continue;//没点友人

                        friendClicked = true;
                        break;
                    }
                    if (friendClicked) friend_stage = 1;
                    else friend_stage = 0;
                }

            }
            else
            {
                friend_outgoingUsed = 0;
            }
                        
        }
    }
}
