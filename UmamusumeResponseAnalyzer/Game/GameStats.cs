using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;

namespace UmamusumeResponseAnalyzer.Game
{

    public class TrainStats
    {
        public int[] FiveValueGain;
        public int PtGain;
        public int[] FiveValueGainNonScenario;
        public int PtGainNonScenario;
        public int VitalGain;
        public int FailureRate;
        public double ScoreAssumeSuccessNoVital()
        {
            return FiveValueGain.Sum() + PtGain * 0.7;
        }
        public double ScoreNoVital()
        {
            const double scoreAssumeFailed = -100;//掉1心情5属性，还相当于浪费一回合，有各种隐性影响
            return ScoreAssumeSuccessNoVital() * (1 - 0.01 * FailureRate) + 0.01 * FailureRate * scoreAssumeFailed;
        }
        public double Score(int currentVital, int maxVital)
        {
            double scoreAssumeFailed = -100;
            double scoreAssumeSuccess = FiveValueGain.Sum() + PtGain * 0.7;
            return (scoreAssumeSuccess - ScoreUtils.ScoreOfVital(currentVital, maxVital) + ScoreUtils.ScoreOfVital(currentVital + VitalGain, maxVital)) * (1 - 0.01 * FailureRate) + 0.01 * FailureRate * scoreAssumeFailed;
        }
    }
    public class TurnStats
    {
        public bool isTraining;//是否为训练回合（否则为比赛回合）
        public int motivation;//干劲（心情）
        public TrainStats[] fiveTrainStats;//五个训练分别加多少

        public int playerChoice;//玩家最终点了哪个训练
        public bool isTrainingFailed;//是否训练失败
        public int[] trainLevel;//训练等级
        public int[] trainLevelCount;//训练等级计数，凯旋门每4为一级


        //凯旋门
        public bool[] larc_zuoyueAtTrain;//佐岳是否在这个训练
        public bool larc_playerChoiceSS;//这个回合玩家是不是点的ss训练
        public bool larc_isFullSS;//这个回合ss训练有没有5个头
        public bool larc_isSSS;//这个回合是不是sss训练
        public int larc_zuoyueEvent;//是否召唤出佐岳充电事件。有好几种：0没事件，1充电，2充电加心情，4海外，5第一次启动
        public int larc_totalApproval;//玩家与所有npc的总“支援pt”



        //大师杯
        //三女神等级
        public int venus_yellowVenusLevel;
        public int venus_redVenusLevel;
        public int venus_blueVenusLevel;

        public bool venus_isEffect102;//是否为女神情热状态
        public int venus_venusTrain;//女神在哪个训练
        public bool venus_isVenusCountConcerned;//非情热，且无蓝女神
        public bool venus_venusEvent;//成功召唤出女神三选一事件
        public TurnStats() {
            isTraining = false;
            motivation = 0;
            playerChoice = -1;
            isTrainingFailed = false;
            fiveTrainStats = new TrainStats[5];
            trainLevel = new int[5];
            trainLevelCount = new int[5];
            for (int j = 0; j < 5; j++) trainLevel[j] = 1;
            for (int j = 0; j < 5; j++) trainLevelCount[j] = 0;

            larc_zuoyueAtTrain = new bool[5];//佐岳是否在这个训练
            for(int j = 0; j < 5; j++) larc_zuoyueAtTrain[j] = false;
            larc_playerChoiceSS = false;
            larc_isFullSS = false;
            larc_isSSS = false;
            larc_zuoyueEvent = 0;
            larc_totalApproval = 0;

            venus_isEffect102 = false;
            venus_yellowVenusLevel = 0;
            venus_redVenusLevel = 0;
            venus_blueVenusLevel = 0;
            venus_venusTrain = -100;
            venus_isVenusCountConcerned = true;
            venus_venusEvent = false;
        }
    }
    public class GameStats
    {
        public static bool isFullGame = false;//小黑板是否从游戏一开始就开着。如果不是，会丢失一些统计信息
        public static int whichScenario = 0;
        public static int currentTurn = 0;
        public static TurnStats[] stats=new TurnStats[79];//从1开始

        public static void print()
        {
            //统计掉心情次数
            {
                int motDropCount = 0;
                for (int i = currentTurn; i >= 2; i--)
                {
                    if (stats[i] == null)
                    {
                        break;
                    }
                    if (stats[i - 1] == null)
                    {
                        break;
                    }
                    if (stats[i].motivation < stats[i - 1].motivation)
                        motDropCount += (stats[i - 1].motivation - stats[i].motivation);
                }
                AnsiConsole.MarkupLine($"这局掉了[yellow]{motDropCount}[/]次心情（忽略刚掉就回的情况）");
            }

            //统计体力消耗和赌训练的次数
            {
                int totalVitalGain = 0;
                int totalGambleTimes = 0;
                int totalFailureRate = 0;
                int totalFailureTimes = 0;
                for (int i = currentTurn - 1; i >= 1; i--)
                {
                    if (stats[i] == null)
                    {
                        break;
                    }
                    if (!GameGlobal.TrainIds.Any(x => x == stats[i].playerChoice)) //没训练
                        continue;
                    var trainStat = stats[i].fiveTrainStats[GameGlobal.ToTrainIndex[stats[i].playerChoice]];
                    if (!stats[i].isTrainingFailed)
                        totalVitalGain += trainStat.VitalGain;
                    else
                        totalFailureTimes += 1;
                    int failRate = trainStat.FailureRate;
                    if(failRate>0)
                    {
                        totalGambleTimes += 1;
                        totalFailureRate += failRate;
                    }
                }
                AnsiConsole.MarkupLine($"这局赌了[yellow]{totalGambleTimes}[/]次训练，失败了[yellow]{totalFailureTimes}[/]次，总失败率为[yellow]{totalFailureRate}[/]%");
                AnsiConsole.MarkupLine($"训练消耗总体力：[yellow]{-totalVitalGain}[/]");
            }
            if (whichScenario == (int)ScenarioType.GrandMasters)
            {
                //统计召唤女神次数与来的次数
                {
                    int concernedTurnTotal = 0, venusEventCount = 0;
                    for (int turn = 1; turn < currentTurn; turn++)//不考虑当前回合，因为当前回合还未选训练
                    {
                        var turnStat = stats[turn];
                        if (turnStat == null) continue;//有可能这局中途才打开小黑板
                        if (turnStat.isTraining && turnStat.venus_isVenusCountConcerned && turnStat.venus_venusTrain == turnStat.playerChoice)
                        {

                            concernedTurnTotal += 1;
                            if (turnStat.venus_venusEvent)
                            {
                                venusEventCount += 1;
                            }
                        }
                    }
                    AnsiConsole.MarkupLine($"一共召唤了[aqua]{concernedTurnTotal}[/]次女神，女神来了[aqua]{venusEventCount}[/]次");
                }
                //统计女神持续回合数
                {
                    int[] contTurns = new int[100];
                    int maxContTurns = 0;
                    for (int i = 0; i < 100; i++) { contTurns[i] = 0; }

                    bool isUnfinishedLast = true;//最后一次不完整，不统计
                    int contTurn = 0;
                    for (int i = currentTurn; i >= 1; i--)
                    {
                        //第一个回合不可能是女神情热，所以这个判断条件没有问题
                        if (stats[i] == null)
                        {
                            break;
                        }
                        if (!stats[i].venus_isEffect102)
                        {
                            if (!isUnfinishedLast)
                            {
                                contTurns[contTurn]++;
                                if (contTurn > maxContTurns) maxContTurns = contTurn;
                            }
                            contTurn = 0;
                            isUnfinishedLast = false;
                        }
                        else contTurn++;
                    }
                    string linetoPrint = $"女神持续回合数统计：";
                    for (int i = 1; i <= maxContTurns; i++)
                    {
                        linetoPrint += $"[green]{i}[/]回合[yellow]{contTurns[i]}[/]次，";
                    }
                    AnsiConsole.MarkupLine(linetoPrint);
                }

                //统计训练属性pt收益
                {
                    int[] fiveGain = new int[5] { 0, 0, 0, 0, 0 }; //总训练
                    int ptGain = 0;
                    int[] fiveGainSpirit = new int[5] { 0, 0, 0, 0, 0 }; //碎片
                    int ptGainSpirit = 0;
                    for (int turn = currentTurn - 1; turn >= 1; turn--)
                    {
                        if (stats[turn] == null)
                        {
                            break;
                        }
                        if (stats[turn].fiveTrainStats == null || stats[turn].fiveTrainStats[0] == null || stats[turn].fiveTrainStats[0].FiveValueGain == null)
                        {
                            continue;
                        }
                        if (!GameGlobal.TrainIds.Any(x => x == stats[turn].playerChoice)) //没训练
                            continue;
                        if (stats[turn].isTrainingFailed)//训练失败
                            continue;
                        var trainStat = stats[turn].fiveTrainStats[GameGlobal.ToTrainIndex[stats[turn].playerChoice]];
                        double[] venusLevelBonus = new double[6] { 0, 0.05, 0.08, 0.11, 0.13, 0.15 };//女神等级训练加成
                        double venusBonus = venusLevelBonus[stats[turn].venus_blueVenusLevel] + venusLevelBonus[stats[turn].venus_yellowVenusLevel] + venusLevelBonus[stats[turn].venus_redVenusLevel];

                        for (int i = 0; i < 5; i++)
                        {
                            int gain = trainStat.FiveValueGain[i];
                            int gainNonScenario = trainStat.FiveValueGainNonScenario[i];
                            int gainNonSpirit = (int)(((double)gainNonScenario) * (1 + venusBonus));
                            int gainSpirit = gain - gainNonSpirit;

                            fiveGain[i] += gain;
                            fiveGainSpirit[i] += gainSpirit;
                        }
                        {
                            int gain = trainStat.PtGain;
                            int gainNonScenario = trainStat.PtGainNonScenario;
                            int gainNonSpirit = (int)(((double)gainNonScenario) * (1 + venusBonus));
                            int gainSpirit = gain - gainNonSpirit;

                            ptGain += gain;
                            ptGainSpirit += gainSpirit;
                        }

                    }

                    var table = new Table();
                    int tableWidth = 7;
                    table.AddColumns(
                          new TableColumn($" ").Width(12),
                          new TableColumn($"速").Width(tableWidth),
                          new TableColumn($"耐").Width(tableWidth),
                          new TableColumn($"力").Width(tableWidth),
                          new TableColumn($"根").Width(tableWidth),
                          new TableColumn($"智").Width(tableWidth),
                          new TableColumn($"总").Width(tableWidth),
                          new TableColumn($"pt").Width(tableWidth)
                          );
                    {
                        var outputItems = new string[8];
                        outputItems[0] = "总训练";
                        for (int j = 0; j < 5; j++)
                            outputItems[j + 1] = $"{fiveGain[j]}";
                        outputItems[6] = $"{fiveGain.Sum()}";
                        outputItems[7] = $"{ptGain}";
                        table.AddRow(outputItems);
                    }
                    {
                        var outputItems = new string[8];
                        outputItems[0] = "碎片加成";
                        for (int j = 0; j < 5; j++)
                            outputItems[j + 1] = $"{fiveGainSpirit[j]}";
                        outputItems[6] = $"{fiveGainSpirit.Sum()}";
                        outputItems[7] = $"{ptGainSpirit}";
                        table.AddRow(outputItems);
                    }

                    AnsiConsole.Write(table);
                }
            }
            if (whichScenario == (int)ScenarioType.LArc)
            {
                //几次ss训练，几次sss，连着几回合没sss
                int fullSSCount = 0;
                int SSSCount = 0;
                int contNonSSS = 0;
                for (int i = currentTurn; i >= 1; i--)
                {
                    if (stats[i] == null)
                    {
                        break;
                    }

                    if (stats[i].larc_isFullSS && stats[i].larc_playerChoiceSS)//一次完整的ss训练
                    {
                        fullSSCount += 1;
                        if (stats[i].larc_isSSS) SSSCount += 1;
                        if (SSSCount == 0) contNonSSS += 1;
                    }
                }
                AnsiConsole.MarkupLine($"一共进行了[aqua]{fullSSCount}[/]次SS训练，其中[aqua]{SSSCount}[/]次为SSS，已经连续[#80ff00]{contNonSSS}[/]次不是SSS");


                //佐岳点了几次，来了几次
                int zuoyueClickedTimesNonAbroad = 0;//非海外点了几次，不算启动
                int zuoyueClickedTimesAbroad = 0;//海外点了几次
                int zuoyueChargedTimes = 0;//友人冲了几次电（非海外）
                int zuoyueEventTimesAbroad = 0;//海外来了几次

                for (int turn = currentTurn; turn >= 1; turn--)
                {
                    if (stats[turn] == null)
                    {
                        break;
                    }
                    bool isAbroad = (turn >= 37 && turn <= 43) || (turn >= 61 && turn <= 67);

                    if (!GameGlobal.TrainIds.Any(x => x == stats[turn].playerChoice)) //没训练
                        continue;
                    if (stats[turn].isTrainingFailed)//训练失败
                        continue;
                    if (!stats[turn].larc_zuoyueAtTrain[GameGlobal.ToTrainIndex[stats[turn].playerChoice]])
                        continue;//没点佐岳
                    if (stats[turn].larc_zuoyueEvent == 5)//启动事件
                        continue;//没点佐岳

                    if(isAbroad)
                    {
                        zuoyueClickedTimesAbroad += 1;
                        if (stats[turn].larc_zuoyueEvent == 4)
                            zuoyueEventTimesAbroad += 1;
                    }
                    else
                    {
                        zuoyueClickedTimesNonAbroad += 1;
                        if (stats[turn].larc_zuoyueEvent == 1 || stats[turn].larc_zuoyueEvent == 2)
                            zuoyueChargedTimes += 1;
                    }
                }

                AnsiConsole.MarkupLine($"远征点了[#80ff00]{zuoyueClickedTimesAbroad}[/]次佐岳，加了[#80ff00]{zuoyueEventTimesAbroad}[/]次适性pt");
                AnsiConsole.MarkupLine($"非远征点了[aqua]{zuoyueClickedTimesNonAbroad}[/]次佐岳，充了[aqua]{zuoyueChargedTimes}[/]次电");
            }
        }

#if WRITE_GAME_STATISTICS
        //把统计数据保存在$"./gameStatistics/{type}"下的一个随机文件名的文件中，便于统计
        public static void writeGameStatistics(string type,string jsonStr)
        {
            var statsDirectory = $"./gameStatistics/{type}";//各种游戏统计信息存这里
            if (!Directory.Exists(statsDirectory))
            {
                Directory.CreateDirectory(statsDirectory);
            }
            //生成随机文件名
            string fname = statsDirectory + "/" + Path.GetRandomFileName() + Path.GetRandomFileName() + ".json";
            File.WriteAllText(fname, jsonStr);

        }

        //凯旋门剧本，保存各种统计信息，便于统计各种概率
        //BeforeTrain是这回合分配人头后就保存，需要stats[currentTurn]，比赛回合不需要计算
        //LastTurn是上回合的信息（例如佐岳有没有充电），只需要stats[currentTurn-1]，比赛回合也要计算
        public static void LArcWriteStatsBeforeTrain(Gallop.SingleModeCheckEventResponse @thisTurnEvent)
        {
            if (@thisTurnEvent.data.chara_info.turn != currentTurn)
            {
                AnsiConsole.MarkupLine($"[#ff3000]错误：保存统计数据时回合数不正确！{@thisTurnEvent.data.chara_info.turn}，{currentTurn}[/]");
                return;
            }



            //ss和sss训练的顺序，用01字符串表示，0是ss，1是sss
            //只在恰好攒够5个头的回合保存（不在点ss训练的回合保存是因为可能杀马
            //同一局每次ss都保存，所以假如一局是0100101，那么它会保存7个文件：0，01，010，0100，01001，010010，0100101
            //当很多局的数据混在一起时，可以这样分离：先找最长的那个，然后删掉它和它所有的前子串（比如0100101会删掉0，01，010，0100，01001，010010，0100101），这样就分离出来了这局的数据
            //重复这个过程，直到空

            //计算turn这个回合是不是新的ss训练
            bool isNewSS(int turn) {
                if (turn <= 2 || turn >= 60) return false;
                if (turn > currentTurn)
                {
                    AnsiConsole.MarkupLine($"[#ff3000]错误：isNewSS turn>currentTurn  {turn}，{currentTurn}[/]");
                    return false;
                }

                //先检查这个回合是不是恰好攒够5个头
                if (stats[turn].larc_isFullSS)
                {
                    int lastTurn =
                        turn == 13 ? 11 : //出道赛
                        turn == 35 ? 33 : //日本德比
                        turn == 44 ? 36 : //第一次远征
                        turn - 1;//远征刚结束的第一个回合（44）的前一个回合是6月后半（36）

                    //是不是新的ss
                    if ((!stats[lastTurn].larc_isFullSS) || //上回合不是满ss，这回合是
                        stats[lastTurn].larc_playerChoiceSS) //上回合点的ss，加这个判断是考虑到连出两个ss的情况
                    {
                        return true;
                    }
                }
                return false;
            }
            if(isNewSS(currentTurn))
            {
                string ssHistory = "";
                for (int turn = 3; turn <= currentTurn; turn++)
                {
                    if (isNewSS(turn))
                    {
                        if (stats[turn].larc_isSSS)
                            ssHistory += "1";
                        else
                            ssHistory += "0";
                    }
                }
                writeGameStatistics("SSS_Statistics", ssHistory);
            }

            bool haveSSRzuoyue = false;
            int zuoyueIndex = 0;
            var supportCards = @thisTurnEvent.data.chara_info.support_card_array.ToDictionary(x => x.position, x => x.support_card_id); //当前S卡卡组
            foreach(var card in supportCards)
            {
                var cardIndex = card.Key;
                var cardId = card.Value;
                if(cardId == 30160 || cardId == 10094)
                {
                    if (cardId == 30160)
                    {
                        haveSSRzuoyue = true;
                        zuoyueIndex = cardIndex;
                    }
                }
            }

            if(haveSSRzuoyue)//统计友人出现在哪几个训练
            {
                int jiban = @thisTurnEvent.data.chara_info.evaluation_info_array.First(x => x.target_id == zuoyueIndex).evaluation;
                var t = stats[currentTurn].larc_zuoyueAtTrain;
                var toWriteStr = $"{currentTurn} {jiban} {(t[0] ? 1 : 0)}{(t[1] ? 1 : 0)}{(t[2] ? 1 : 0)}{(t[3] ? 1 : 0)}{(t[4] ? 1 : 0)}";
                writeGameStatistics("Zuoyue_AtTrain", toWriteStr);
            }





        }

        public static void LArcWriteStatsLastTurn(Gallop.SingleModeCheckEventResponse @thisTurnEvent)
        {
            if (@thisTurnEvent.data.chara_info.turn != currentTurn)
            {
                AnsiConsole.MarkupLine($"[#ff3000]错误：保存统计数据时回合数不正确！{@thisTurnEvent.data.chara_info.turn}，{currentTurn}[/]");
                return;
            }




            bool haveSSRzuoyue = false;
            int zuoyueIndex = 0;
            int linkCardsNum = 0;
            var supportCards = @thisTurnEvent.data.chara_info.support_card_array.ToDictionary(x => x.position, x => x.support_card_id); //当前S卡卡组
            foreach (var card in supportCards)
            {
                var cardIndex = card.Key;
                var cardId = card.Value;
                if (cardId == 30160 || cardId == 10094)
                {
                    linkCardsNum += 1;
                    if (cardId == 30160)
                    {
                        haveSSRzuoyue = true;
                        zuoyueIndex = cardIndex;
                    }
                }
                else if (@thisTurnEvent.data.arc_data_set.evaluation_info_array.Any(x => x.target_id == cardIndex))
                {
                    var chara_id = @thisTurnEvent.data.arc_data_set.evaluation_info_array.First(x => x.target_id == cardIndex).chara_id;
                    if (GameGlobal.LArcScenarioLinkCharas.Any(x => x == chara_id))
                        linkCardsNum += 1;
                }
            }
            //统计友人的召唤率
            if (haveSSRzuoyue)
            {
                bool clickOnZuoyue = true;
                int turn = currentTurn - 1;
                //bool isAbroad = (turn >= 37 && turn <= 43) || (turn >= 61 && turn <= 67);

                if (stats[turn] == null)
                    clickOnZuoyue = false;

                else if (!GameGlobal.TrainIds.Any(x => x == stats[turn].playerChoice)) //没训练
                    clickOnZuoyue = false;
                else if (stats[turn].isTrainingFailed)//训练失败
                    clickOnZuoyue = false;
                else if (!stats[turn].larc_zuoyueAtTrain[GameGlobal.ToTrainIndex[stats[turn].playerChoice]])//没点佐岳
                    clickOnZuoyue = false;
                else if (stats[turn].larc_zuoyueEvent == 5)//启动事件
                    clickOnZuoyue = false;

                if (clickOnZuoyue)
                {
                    string toWriteStr = $"{turn} {stats[turn].larc_zuoyueEvent} {linkCardsNum}";
                    writeGameStatistics("Zuoyue_Event", toWriteStr);
                }
            }






        }
#endif
    }

}
