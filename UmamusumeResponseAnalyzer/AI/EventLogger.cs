using Gallop;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Numerics;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.AI
{
    public class LogValue
    {
        public int stats = 0;
        public int pt = 0;
        public int vital = 0;
        private string fmt(int x) { return x.ToString("+#;-#;0"); }
        public string explain()
        {
            // return $">> 属性: {fmt(stats)}, Pt: {fmt(pt)}, 体力: {fmt(vital)}；事件强度: {eventStrength}";
            return $"属性: {fmt(stats)}, Pt: {fmt(pt)}, 体力: {fmt(vital)}";
        }
        public static LogValue operator -(LogValue a, LogValue b)
        {
            return new LogValue
            {
                stats = a.stats - b.stats,
                pt = a.pt - b.pt,
                vital = a.vital - b.vital
            };
        }

        public bool isEmpty()
        {
            return (stats == 0 && pt == 0 && vital == 0);
        }

        // 事件强度(ES)
        // 暂时按1es = 1属性+1pt, 1体力=2属性估算
        public double eventStrength {
            get {
                return (stats + pt) / 2 + vital;
            }
        }
    }
    public class LogEvent
    {
        public int turn = 0;
        public int story_id = -1;
        public LogValue value;

        public LogEvent() { }
        public LogEvent(LogEvent ev) {
            turn = ev.turn;
            story_id = ev.story_id;
            value = new LogValue
            {
                pt = ev.value.pt,
                vital = ev.value.vital,
                stats = ev.value.stats
            };
        }
    }
    public static class EventLogger
    {
        // 排除佐岳充电,SS,继承,第三年凯旋门（输/赢）,以及无事发生直接到下一回合的情况
        public static int[] ExcludedEvents = { 809043003, 400006112, 400000040, 400006474, 400006439, -1 }; 
        public const int MinEventStrength = 3;

        public static List<LogEvent> cardEvents, allEvents; // 支援卡事件，全部事件（除去排除的）
        public static int cardEventCount = 0;   // 连续事件发生数
        public static int cardEventFinishCount = 0; // 连续事件完成数
        public static int cardEventFinishTurn = 0;  // 如果连续事件全走完，记录回合数
        public static List<int> inheritStats;   // 两次继承属性

        public static LogValue lastValue;   // 前一次调用时的总属性
        public static LogEvent lastEvent;   // 本次调用时已经结束的事件
        public static bool isStart = false;
        
        // 获取当前的属性
        public static LogValue capture(SingleModeCheckEventResponse @event)
        {
            // sanity check
            if (@event.data.chara_info == null)
                return new LogValue();
            else
            {
                var currentFiveValue = new int[]
                {
                    @event.data.chara_info.speed,
                    @event.data.chara_info.stamina,
                    @event.data.chara_info.power,
                    @event.data.chara_info.guts,
                    @event.data.chara_info.wiz,
                };
                var currentFiveValueRevised = currentFiveValue.Select(x => ScoreUtils.ReviseOver1200(x)).ToArray();
                var totalValue = currentFiveValueRevised.Sum();
                int pt = @event.data.chara_info.skill_point;
                int vital = @event.data.chara_info.vital;
                return new LogValue()
                {
                    stats = totalValue,
                    pt = pt,
                    vital = vital
                };
            }
        }

        // 估计连续事件出率
        // 一局基本测不准，印象里是30%，具体待查。
        public static string estimateCardEventRate(int turn)
        {
            // 这些回合不能触发连续事件
            int[] excludedTurns = { 1, 31, 37, 38, 39, 40, 41, 42, 43, 55, 61, 62, 63, 64, 65, 66, 67 };
            // 马娘个人事件占用回合数
            const int umaEventTurns = 3;
            const double rate = 1.65;   // 90%置信度

            // 如果支援卡事件都走完了，固定计算结果
            if (cardEventFinishTurn > 0)
                turn = cardEventFinishTurn;
            // 减去不能触发的回合，根据剩下的回合数对支援卡出率进行回归
            int remainTurns = turn - excludedTurns.Count(x => x <= turn) - umaEventTurns;
            if (remainTurns > 5 && cardEventCount > 0)
            {
                double avg = (double)cardEventCount / remainTurns;
                double stddev = Math.Sqrt(cardEventCount) / remainTurns;
                return $"连续事件出率估计: [yellow]{(avg*100).ToString("0.0")}±{(stddev*rate*100).ToString("0.0")}%[/]";
            }
            else return "";
        }

        //--------------------------
        public static void init()
        {
            cardEvents = new List<LogEvent>();
            allEvents = new List<LogEvent>();
            inheritStats = new List<int>();
            cardEventCount = 0;
            cardEventFinishTurn = 0;
            cardEventFinishCount = 0;
            isStart = false;
        }
        // 开始记录属性变化
        public static void start(SingleModeCheckEventResponse @event)
        {
            lastValue = capture(@event);
            lastEvent = new LogEvent();
            isStart = true;
        }

        // 结束记录前一个事件的属性变化，并保存
        public static void update(SingleModeCheckEventResponse @event)
        {
            if (isStart && @event.data.unchecked_event_array != null)
            {
                // 获得上一个事件的属性并保存
                LogValue currentValue = capture(@event); 
                lastEvent.value = currentValue - lastValue;

                // 分析事件
                // 过滤掉特判的、不加属性的。pt<0的是因为点了技能，会干扰统计，也排除掉
                int eventType = lastEvent.story_id / 100000000;
                if (!lastEvent.value.isEmpty() && !ExcludedEvents.Contains(lastEvent.story_id) && lastEvent.value.pt >= 0)
                {
                    if (eventType == 8)
                    {
                        // 支援卡事件，如"8 30161 003"
                        int rarity = lastEvent.story_id / 10000000 % 10;    // 取第二位-稀有度
                        int which = lastEvent.story_id % 100;   // 取低2位
                        if (rarity > 1 && which <= rarity)    // 是连续事件
                        {
                            ++cardEventCount;
                            if (which == rarity)
                            {
                                ++cardEventFinishCount;    // 走完了N个事件（N是稀有度）则认为连续事件走完了，不考虑断事件
                                if (cardEventFinishCount == 5)
                                    cardEventFinishTurn = @event.data.chara_info.turn;
                                AnsiConsole.MarkupLine($"[yellow]连续事件完成[/]");
                            }
                            else
                                AnsiConsole.MarkupLine($"[yellow]连续事件 {which} / {rarity}[/]");
                        }
                        cardEvents.Add(new LogEvent(lastEvent));
                        allEvents.Add(new LogEvent(lastEvent));
                        AnsiConsole.WriteLine($">> {lastEvent.value.explain()}");
                    }
                    else
                    {
                        // 马娘或系统事件
                        double st = lastEvent.value.eventStrength;
                        if (st < 0 || st >= MinEventStrength) // 过滤掉蚊子腿事件（<0是坏事件，需要留着）
                        {
                            allEvents.Add(new LogEvent(lastEvent));
                            AnsiConsole.WriteLine($">> #{lastEvent.story_id}: {lastEvent.value.explain()}");
                        }
                    }

                }
                else
                {
                    // 分析特殊事件
                    if (lastEvent.story_id == 400000040)    // 继承
                    {
                        AnsiConsole.MarkupLine($"[yellow]本次继承属性：{lastEvent.value.stats}[/]");
                        inheritStats.Add(lastEvent.value.stats);
                    }
                }
                // 保存当前回合数和story_id到lastEvent，用于下次调用
                lastValue = currentValue;
                lastEvent.turn = @event.data.chara_info.turn;
                lastEvent.story_id = (@event.data.unchecked_event_array.Count() > 0 ? @event.data.unchecked_event_array.First().story_id : -1);
            }
        }
    }

}
