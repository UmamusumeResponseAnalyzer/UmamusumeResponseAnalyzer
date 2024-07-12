using Gallop;
using Spectre.Console;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;

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
            // return $">> 属性: {fmt(stats)}, Pt: {fmt(pt)}, 体力: {fmt(vital)}；事件强度: {eventStrength}";
            return $"属性: {fmt(Stats)}, Pt: {fmt(Pt)}, 体力: {fmt(Vital)}";
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
                return (Stats + Pt) / 2 + Vital;
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
        public const int MinEventStrength = 3;
        // 排除佐岳充电,SS,继承,第三年凯旋门（输/赢）,以及无事发生直接到下一回合的情况
        public static readonly int[] ExcludedEvents = [809043003, 400006112, 400000040, 400006474, 400006439, -1];
        // 友人和团队卡不计入连续事件，这里仅排除这几个
        public static readonly int[] ExcludedFriendCards = [30160, 30137, 30067, 30052];
        // 这些回合不能触发连续事件
        private static readonly int[] ExcludedTurns = [1, 31, 37, 38, 39, 40, 41, 42, 43, 55, 61, 62, 63, 64, 65, 66, 67];

        public static List<LogEvent> CardEvents = []; // 支援卡事件
        public static List<LogEvent> AllEvents = []; // 全部事件（除去排除的）
        public static int CardEventCount = 0;   // 连续事件发生数
        public static int CardEventFinishCount = 0; // 连续事件完成数
        public static int CardEventFinishTurn = 0;  // 如果连续事件全走完，记录回合数
        public static int SuccessEventCount = 0;    // 赌狗事件发生数
        public static int SuccessEventSelectCount = 0;  // 赌的次数
        public static int SuccessEventSuccessCount = 0; // 成功数
        public static int CurrentScenario = 0;  // 记录当前剧本，用于判断成功事件
        public static List<int> InheritStats;   // 两次继承属性

        public static LogValue LastValue;   // 前一次调用时的总属性
        public static LogEvent LastEvent;   // 本次调用时已经结束的事件
        public static bool IsStart = false;

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

        // 估计连续事件出率
        // 一局基本测不准，印象里是30%，具体待查。
        public static string EstimateCardEventRate(int turn)
        {
            // 马娘个人事件占用回合数
            const int umaEventTurns = 3;
            const double rate = 1.65;   // 90%置信度

            // 如果支援卡事件都走完了，固定计算结果
            if (CardEventFinishTurn > 0) turn = CardEventFinishTurn;

            // 减去不能触发的回合，根据剩下的回合数对支援卡出率进行回归
            var remainTurns = turn - ExcludedTurns.Count(x => x <= turn) - umaEventTurns;
            if (remainTurns > 5 && CardEventCount > 0) return string.Empty;

            var avg = (double)CardEventCount / remainTurns;
            var stddev = Math.Sqrt(CardEventCount) / remainTurns;
            return $"连续事件出率估计: [yellow]{avg * 100:0.0}±{stddev * rate * 100:0.0}%[/]";
        }

        public static void Print(string s)
        {
            // 以后可能用别的打印方式
            AnsiConsole.MarkupLine(s);
        }

        //--------------------------
        public static void Init()
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
                            ++CardEventCount;
                            if (which == rarity)
                            {
                                ++CardEventFinishCount;    // 走完了N个事件（N是稀有度）则认为连续事件走完了，不考虑断事件
                                if (CardEventFinishCount == 5)
                                    CardEventFinishTurn = @event.data.chara_info.turn;
                                Print($"[yellow]连续事件完成[/]");
                            }
                            else
                            {
                                Print($"[yellow]连续事件 {which} / {rarity}[/]");
                            }
                        }
                        CardEvents.Add(new LogEvent(LastEvent));
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
                        Print($"[yellow]本次继承属性：{LastEvent.Stats}, Pt: {LastEvent.Pt}[/]");
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
            int choiceIndex = @event.choice_number - 1;
            if (LastEvent != null)
            {
                if (Database.SuccessEvent.TryGetValue(LastEvent.StoryId, out var successEvent)
                        && successEvent.Choices.Length > choiceIndex
                        && LastEvent.ChoiceResult != null) //是可以成功的事件，已在数据库中，已经记录到选项结果
                {
                    ++SuccessEventCount;
                    // 判断当前选项是否有成功、失败的区别
                    bool choiceCanSuccess = successEvent.Choices[choiceIndex].Any(x => x.State > 0 && x.State != State.None
                        && (x.Scenario == 0 || x.Scenario == CurrentScenario));
                    if (choiceCanSuccess)
                    {
                        ++SuccessEventSelectCount;
                        int selectIndex = LastEvent.ChoiceResult[choiceIndex].select_index;    // 获得EventLogger之前记录的选项结果
                        // 判断当前选项是否成功
                        var choice = successEvent.Choices[choiceIndex].FirstOrDefault(
                            x => x.SelectIndex == selectIndex && (x.Scenario == 0 || x.Scenario == CurrentScenario),
                            new());
                        if (choice.State > 0 && choice.State != State.None)
                            ++SuccessEventSuccessCount; // 暂时不区分大成功
                    }
                    
                }
            }
        }
    }

}
