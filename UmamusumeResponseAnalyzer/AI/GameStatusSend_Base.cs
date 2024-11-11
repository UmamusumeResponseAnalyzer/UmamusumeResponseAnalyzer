using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using UmamusumeResponseAnalyzer.Game;
using Spectre.Console;
using UmamusumeResponseAnalyzer.Communications.Subscriptions;
using MathNet.Numerics.RootFinding;
using Newtonsoft.Json;
using System.IO.IsolatedStorage;

namespace UmamusumeResponseAnalyzer.AI
{
    /// <summary>
    /// 传递给AI的人头信息的基本接口（测试）
    /// </summary>
    public class PersonBase
    {
        //0代表未加载（例如前两个回合的npc），1代表友人（R或SSR都行），2代表普通支援卡，3代表npc人头，4理事长，5记者，6不带卡的佐岳。暂不支持其他友人/团队卡
        public int personType = 0;
        //int16_t cardId;//支援卡id，不是支援卡就0
        //npc人头的马娘id，不是npc就0，懒得写也可以一律0（只用于获得npc的名字）
        public int charaId = 0;
        //羁绊
        public int friendship = 0;
        //bool atTrain[5];//是否在五个训练里。对于普通的卡只是one-hot或者全空，对于ssr佐岳可能有两个true
        //bool isShining;//是否闪彩。无法闪彩的卡或者npc恒为false
        //是否有hint。友人卡或者npc恒为false
        public bool isHint = false;
        //记录一些可能随着时间而改变的参数，例如根涡轮的固有
        public int cardRecord = 0;

    }
    public class GameStatusSend_Base<T>
    where T: PersonBase, new()
    {
        public int umaId;//马娘编号，见KnownUmas.cpp
        public int umaStar;//几星
        public bool islegal;//是否为有效的回合数据

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
        public T[] persons;//依次是6张卡
        public int[,] personDistribution;//每个训练有哪些人头id，personDistribution[哪个训练][第几个人头]，空人头为-1
        public int lockedTrainingId;
        public int friendship_noncard_yayoi;//非卡理事长的羁绊，带了理事长卡就是0
        public int friendship_noncard_reporter;//非卡记者的羁绊       

        //单独处理友人卡，因为接近必带。其他友人团队卡的以后再考虑
        public int friend_type;//0没带友人卡，1 ssr卡，2 r卡
        public int friend_cardId;   // 单独存放友人卡ID
        public int friend_personId;//友人卡在persons里的编号
        public int friend_stage;//0未点击，1点击还未解锁出行，2已解锁出行
        public int friend_outgoingUsed;//出行已经走了几段了   暂时不考虑其他友人团队卡的出行

        /// <summary>
        /// 游戏状态，不为1时，为重复回合
        /// </summary>
        public int playing_state;

        public bool isRepeatTurn()
        {
            return this.playing_state != 1;
        }

        public GameStatusSend_Base(Gallop.SingleModeCheckEventResponse @event)
        {
            islegal = false;
            playing_state = @event.data.chara_info.playing_state;
            if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0)) return;
            if (
                (@event.data.chara_info.playing_state == 1) ||
                (@event.data.chara_info.playing_state == 26 && @event.IsScenario(ScenarioType.Mecha)) 
                )
            {

            }
            else
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
                var ptScore = AiUtils.calculateSkillScore(@event, ptScoreRate);
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

            var trainLevelClickNumEvery = 4;
            var turnStat = GameStats.stats[@event.data.chara_info.turn];
            if (turnStat == null)
            {
                AnsiConsole.MarkupLine($"[yellow]获取训练等级信息出错[/]");
                for (var i = 0; i < 5; i++)
                {
                    var trId = @event.IsScenario(ScenarioType.Mecha) ? GameGlobal.TrainIdsMecha[i] : 
                        GameGlobal.TrainIds[i];
                    var trLevel = @event.data.chara_info.training_level_info_array.First(x => x.command_id == trId).level;
                    var count = (trLevel - 1) * trainLevelClickNumEvery;
                    trainLevelCount[i] = count;
                }
            }
            else
            {
                for (var i = 0; i < 5; i++)
                    trainLevelCount[i] = turnStat.trainLevelCount[i] + trainLevelClickNumEvery * (turnStat.trainLevel[i] - 1);
            }

            //从游戏json的id到ai的人头编号的换算
            foreach (var s in @event.data.chara_info.support_card_array)
            {
                var p = s.position - 1;
                //突破数+10*卡原来的id，例如神团是30137，满破神团就是301374
                cardId[p] = s.limit_break_count + s.support_card_id * 10;
            }

            persons = new T[6];
            for (var i = 0; i < 6; i++)
                persons[i] = new T();

            friend_type = 0;
            friend_personId = 0;
            for (var i = 0; i < 6; i++)
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
            for (var i = 0; i < 5; i++)
                for (var j = 0; j < 5; j++)
                    personDistribution[i, j] = -1;

            foreach (var train in @event.data.home_info.command_info_array)
            {
                //Console.WriteLine(train.command_id);
                if (!GameGlobal.ToTrainIndex.ContainsKey(train.command_id))//不是正常训练
                    continue;
                //Console.WriteLine("!");
                var trainId = GameGlobal.ToTrainIndex[train.command_id];

                var j = 0;
                foreach (var p in train.training_partner_array)
                {
                    var personIdUmaAi = p == 102 ? 6 : p == 103 ? 7 : p >= 1000 ? 8 : p - 1;
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
                var istrainlocked = false;
                var enableidx = -1;
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
                    var friendClicked = false;//友人卡是否点过第一次
                    for (var t = @event.data.chara_info.turn - 1; t >= 1; t--)
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

        public void doSend()
        {
            if (this.islegal == false)
            {
                return;
            }
            var wsSubscribeCount = SubscribeAiInfo.Signal(this);
            if (wsSubscribeCount > 0 && !this.isRepeatTurn())
                AnsiConsole.MarkupLine("\n[aqua]AI计算中...[/]");

            var currentGSdirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "GameData");
            Directory.CreateDirectory(currentGSdirectory);
            var success = false;
            var tried = 0;
            do
            {
                try
                {
                    var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }; // 去掉空值避免C++端抽风
                    File.WriteAllText($@"{currentGSdirectory}/thisTurn.json", JsonConvert.SerializeObject(this, Formatting.Indented, settings));
                    File.WriteAllText($@"{currentGSdirectory}/turn{this.turn}.json", JsonConvert.SerializeObject(this, Formatting.Indented, settings));
                    success = true; // 写入成功，跳出循环
                    break;
                }
                catch
                {
                    tried++;
                    AnsiConsole.MarkupLine("[yellow]写入失败[/]");
                }
            } while (!success && tried < 10);
            if (!success)
            {
                AnsiConsole.MarkupLine($@"[red]写入{currentGSdirectory}/thisTurn.json失败！[/]");
            }
        }
    }
}
