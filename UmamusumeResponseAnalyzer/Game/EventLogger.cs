using Gallop;
using MathNet.Numerics.Distributions;
using Spectre.Console;
using System.Globalization;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Handler;

namespace UmamusumeResponseAnalyzer.Game
{
    public class LogValue
    {
        public static LogValue NULL = new();
        public int Stats = 0;
        public int Pt = 0;
        public int Vital = 0;
        private string fmt(int x) => x.ToString("+#;-#;0");
        public string Explain()
        {
            return $">> 属性: {fmt(Stats)}, Pt: {fmt(Pt)}, 体力: {fmt(Vital)}；评分: +{EventStrength}";
            // return $"属性: {fmt(Stats)}, Pt: {fmt(Pt)}, 体力: {fmt(Vital)}";
        }
        public static LogValue operator -(LogValue a, LogValue b)
        {
            return new LogValue
            {
                Stats = a.Stats - b.Stats,
                Pt = a.Pt - b.Pt,
                Vital = a.Vital - b.Vital
            };
        }

        public bool IsEmpty { get => Stats == 0 && Pt == 0 && Vital == 0; }

        // 事件强度(ES)
        // 暂时按1es = 1属性+1pt, 1体力=2属性估算
        public double EventStrength
        {
            get
            {
                return Stats*4 + Pt*2 + Vital*6;
            }
        }
    }
    public class LogEvent
    {
        public LogValue Value;
        public int Turn = 0;
        public int StoryId = -1;
        public ChoiceArray[]? ChoiceResult;  // 透视值
        public int Pt => Value.Pt;
        public int Vital => Value.Vital;
        public int Stats => Value.Stats;
        public double EventStrength => Value.EventStrength;

        public LogEvent() { }
        public LogEvent(LogEvent ev)
        {
            Turn = ev.Turn;
            StoryId = ev.StoryId;
            ChoiceResult = ev.ChoiceResult;
            Value = new LogValue
            {
                Pt = ev.Pt,
                Vital = ev.Vital,
                Stats = ev.Stats
            };
        }
    }

    public class CardEventLogEntry
    {
        public int turn = -1;       // 回合数
        //public int eventType = 0;   // 事件类型 4-系统，5-马娘，8-支援卡
        public int cardId = -1;     // 支援卡ID eventType=8时生效
        public int rarity = -1;     // 稀有度
        public int step = 0;        // 连续事件步数
        public bool isFinished = false;
    }

    public static class EventLogger
    {
        public const int MinEventStrength = 25;
        // 排除佐岳充电,SS,继承,第三年凯旋门（输/赢）,以及无事发生直接到下一回合的情况
        public static readonly int[] ExcludedEvents = [809043003, 400006112, 400000040, 400006474, 400006439, -1];
        // 友人和团队卡不计入连续事件，这里仅排除这几个
        public static readonly int[] ExcludedFriendCards = [30160, 30137, 30067, 30052, 10104, 30188];
        // 这些回合不能触发连续事件
        public static readonly int[] ExcludedTurns = [1, 25, 31, 35, 37, 38, 39, 40, 49, 51, 55, 59, 61, 62, 63, 64, 72, 73, 74, 75, 76, 77, 78];

        public static List<LogEvent> CardEvents = []; // 支援卡事件
        public static List<LogEvent> AllEvents = []; // 全部事件（除去排除的）
        public static int CardEventCount = 0;   // 连续事件发生数
        public static int CardEventFinishCount = 0; // 连续事件完成数
        public static int CardEventFinishTurn = 0;  // 如果连续事件全走完，记录回合数
        public static int CardEventRemaining = 0;  // 连续事件剩余数
        public static int SuccessEventCount = 0;    // 赌狗事件发生数
        public static int SuccessEventSelectCount = 0;  // 赌的次数
        public static int SuccessEventSuccessCount = 0; // 成功数
        public static int CurrentScenario = 0;  // 记录当前剧本，用于判断成功事件
        public static List<int> InheritStats;   // 两次继承属性

        public static LogValue LastValue;   // 前一次调用时的总属性
        public static LogEvent LastEvent;   // 本次调用时已经结束的事件
        public static bool IsStart = false;
        public static int InitTurn = 0;    // 调用Init时的起始回合数
        public static List<int> CardIDs;   // 存放配卡，以过滤乱入事件

        // 获取当前的属性
        public static LogValue Capture(SingleModeCheckEventResponse @event)
        {
            // sanity check
            if (@event.data.chara_info == null) return LogValue.NULL;
            var currentFiveValue = new int[]
            {
                    @event.data.chara_info.speed,
                    @event.data.chara_info.stamina,
                    @event.data.chara_info.power,
                    @event.data.chara_info.guts,
                    @event.data.chara_info.wiz,
            };
            var currentFiveValueRevised = currentFiveValue.Select(ScoreUtils.ReviseOver1200);
            var totalValue = currentFiveValueRevised.Sum();
            var pt = @event.data.chara_info.skill_point;
            var vital = @event.data.chara_info.vital;
            CurrentScenario = @event.data.chara_info.scenario_id;
            return new LogValue()
            {
                Stats = totalValue,
                Pt = pt,
                Vital = vital
            };
        }

        public static void Print(string s)
        {
            // 以后可能用别的打印方式
            AnsiConsole.MarkupLine(s);
        }

        //--------------------------
        // 这个方法在重复发送第一回合时会被反复调用，需要可重入
        public static void Init(SingleModeCheckEventResponse @event)
        {
            CardEvents = [];
            AllEvents = [];
            InheritStats = [];
            CardEventCount = 0;
            CardEventFinishTurn = 0;
            CardEventFinishCount = 0;
            SuccessEventCount = 0;
            SuccessEventSuccessCount = 0;
            SuccessEventSelectCount = 0;
            CurrentScenario = 0;
            IsStart = false;
            InitTurn = @event.data.chara_info.turn;
            // 需要传入SupportCard数组以确认带了哪些卡
            CardIDs = @event.data.chara_info.support_card_array.Select(x => x.support_card_id).ToList();
            CardEventRemaining = 0;
            foreach (var c in CardIDs)
            {
                if (!ExcludedFriendCards.Contains(c) && c / 10000 > 1)  // 稀有度>1
                    CardEventRemaining += c / 10000;
            }
        }
        // 开始记录属性变化
        public static void Start(SingleModeCheckEventResponse @event)
        {
            LastValue = Capture(@event);
            LastEvent = new LogEvent();
            IsStart = true;
        }

        // 结束记录前一个事件的属性变化，并保存
        public static void Update(SingleModeCheckEventResponse @event)
        {
            if (IsStart && @event.data.unchecked_event_array != null)
            {
                // 获得上一个事件的属性并保存
                var currentValue = Capture(@event);
                LastEvent.Value = currentValue - LastValue;

                // 分析事件
                // 过滤掉特判的、不加属性的。
                // pt<0的是因为点了技能，会干扰统计，也排除掉
                var eventType = LastEvent.StoryId / 100000000;
                if (!LastEvent.Value.IsEmpty && !ExcludedEvents.Contains(LastEvent.StoryId) && LastEvent.Pt >= 0)
                {
                    if (eventType == 8)
                    {
                        // 支援卡事件，如"8 30161 003"
                        var rarity = LastEvent.StoryId / 10000000 % 10;    // 取第二位-稀有度
                        var which = LastEvent.StoryId % 100;   // 取低2位
                        var cardId = LastEvent.StoryId / 1000 % 100000;
                        if (rarity > 1 && which <= rarity && !ExcludedFriendCards.Contains(cardId))    // 是连续事件
                        {
                            if (CardIDs.Contains(cardId))
                            {
                                ++CardEventCount;
                                --CardEventRemaining;
                                if (which == rarity)
                                {
                                    ++CardEventFinishCount;    // 走完了N个事件（N是稀有度）则认为连续事件走完了，不考虑断事件
                                    if (rarity == 2)
                                        --CardEventRemaining;   // 非SSR的情况下总事件数-1
                                    if (CardEventFinishCount == 5)
                                        CardEventFinishTurn = @event.data.chara_info.turn;
                                    Print($"[yellow]连续事件完成[/]");
                                }
                                else
                                {
                                    Print($"[yellow]连续事件 {which} / {rarity}[/]");
                                }
                            }
                            else
                            {
                                Print($"[red]乱入连续事件[/]");
                            }
                            CardEvents.Add(new LogEvent(LastEvent));
                        }
                        AllEvents.Add(new LogEvent(LastEvent));
                        Print($">> {LastEvent.Value.Explain()}");
                    }
                    else
                    {
                        // 马娘或系统事件
                        var st = LastEvent.EventStrength;
                        if (st < 0 || st >= MinEventStrength) // 过滤掉蚊子腿事件（<0是坏事件，需要留着）
                        {
                            AllEvents.Add(new LogEvent(LastEvent));
                            Print($">> #{LastEvent.StoryId}: {LastEvent.Value.Explain()}");
                        }
                    }
                }
                else
                {
                    // 分析特殊事件
                    if (LastEvent.StoryId == 400000040)    // 继承
                    {
                        Print($"[yellow]本次继承属性：{LastEvent.Stats}[/]");
                        InheritStats.Add(LastEvent.Stats);
                    }
                }
                // 保存当前回合数和story_id到lastEvent，用于下次调用
                LastValue = currentValue;
                LastEvent.Turn = @event.data.chara_info.turn;
                LastEvent.StoryId = @event.data.unchecked_event_array.Count() > 0 ? @event.data.unchecked_event_array.First().story_id : -1;
                LastEvent.ChoiceResult = @event.data.unchecked_event_array.Count() > 0 ? @event.data.unchecked_event_array.First().event_contents_info.choice_array : null;
            }
        }

        // 当玩家选择选项时进行记录
        public static void UpdatePlayerChoice(Gallop.SingleModeChoiceRequest @event)
        {
            var choiceIndex = @event.choice_number - 1;
            if (LastEvent != null)
            {
                Print($"[yellow]玩家选择了 {@event.choice_number}[/]");

                var eventType = LastEvent.StoryId / 100000000;
                var cardId = LastEvent.StoryId / 1000 % 100000;
                var rarity = LastEvent.StoryId / 10000000 % 10;  // 取第二位-稀有度
                var which = LastEvent.StoryId % 100;   // 取低2位
                // 是非友人事件，且记录了事件效果
                if (eventType == 8 &&
                    !ExcludedFriendCards.Contains(cardId) && 
                    Database.Events.TryGetValue(LastEvent.StoryId, out var story) &&
                    story.Choices.Count > choiceIndex &&
                    story.Choices[choiceIndex].Count > 0)
                {
                    var choice = story.Choices[choiceIndex][0];
                    // 判断是否为断事件
                    if (choice.SuccessEffectValue != null && choice.SuccessEffectValue.Extras.Any(x => x.Contains("打ち切り")))
                    {
                        Print(@"[red]事件中断[/]");
                        if (CardIDs.Contains(cardId))             // 排除打断乱入连续事件的情况
                        {
                            ++CardEventFinishCount;
                            CardEventRemaining -= (rarity - which); // 计算打断了几段事件，从总数里减去
                            if (CardEventFinishCount == 5)
                                CardEventFinishTurn = LastEvent.Turn;
                        }
                    }
                }                
            }
        }

        public static List<string> PrintCardEventPerf()
        {
            var ret = new List<string>();
            if (CardEventCount > 0)
            {
                double p = 0.25;
                int n = (GameStats.currentTurn - InitTurn + 1) - ExcludedTurns.Count(x => x >= InitTurn && x <= GameStats.currentTurn);
                //(p(x<=k-1) + p(x<=k)) / 2
                double bn = Binomial.CDF(p, n, CardEventCount);
                double bn_1 = Binomial.CDF(p, n, CardEventCount - 1);

                ret.Add("");
                if (CardEventFinishCount < 5)
                {
                    // 调试中，暂不加入I18N
                    ret.Add(string.Format("连续事件出现[yellow]{0}[/]次", CardEventCount));
                    ret.Add(string.Format("走完[yellow]{0}[/]张卡", CardEventFinishCount));
                    if (InitTurn != 1 && n > 0)
                        ret.Add(string.Format("连续事件运气: [yellow]{0}%[/]", ((bn + bn_1) / 2 * 200 - 100).ToString("+#;-#;0")));
                    else
                    {
                        // 从第1回合开始记录则可以计算连续事件走完率
                        int TurnRemaining = 78 - GameStats.currentTurn - ExcludedTurns.Count(x => x > GameStats.currentTurn); // 还剩多少回合，不包括本回合
                        // p(x>=k) = 1-p(x<=k-1)
                        double pFinish = 0;
                        if (CardEventRemaining <= 0)
                            pFinish = 1;
                        else if (TurnRemaining > 0) 
                            pFinish = 1 - Binomial.CDF(p, TurnRemaining, CardEventRemaining - 1);
                        ret.Add(string.Format("剩余[yellow]{0}[/]个连续事件", CardEventRemaining));
                        ret.Add(string.Format("完成概率: [yellow]{0}%[/]", (pFinish * 100).ToString("0")));
                    }
                }
                else
                {
                    ret.Add(string.Format("[green]连续事件全部完成[/]"));
                }
            }
            return ret;
        }
    }
}
