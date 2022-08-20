using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Localization;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {

        public static void ParseSingleModeCheckEventResponse(Gallop.SingleModeCheckEventResponse @event)
        {
            foreach (var i in @event.data.unchecked_event_array)
            {
                //收录在数据库中
                if (Database.Events.TryGetValue(i.story_id, out var story)
                    && story.Choices.Count == i.event_contents_info.choice_array.Length) //并且数据库中的事件不少选项
                {
                    var mainTree = new Tree(story.TriggerName.EscapeMarkup()); //触发者名称
                    var eventTree = new Tree(story.Name.EscapeMarkup()); //事件名称
                    for (var j = 0; j < i.event_contents_info.choice_array.Length; ++j)
                    {
                        var originalChoice = story.Choices[j][0]; //因为kamigame的事件无法直接根据SelectIndex区分成功与否，所以必然只会有一个Choice;
                        //显示选项
                        var tree = new Tree($"{(string.IsNullOrEmpty(originalChoice.Option) ? Resource.SingleModeCheckEvent_Event_NoOption : originalChoice.Option)} @ {i.event_contents_info.choice_array[j].select_index}".EscapeMarkup());
                        if (Database.SuccessEvent.TryGetValue(i.story_id, out var successEvent) && successEvent.Choices.Length > j) //是可以成功的事件且已在数据库中
                            AddLoggedEvent(successEvent.Choices[j]);
                        else
                            AddNormalEvent();
                        eventTree.AddNode(tree);

                        void AddLoggedEvent(SuccessChoice[] choices)
                        {
                            var find = choices.WithSelectIndex(i.event_contents_info.choice_array[j].select_index)
                                .WithScenarioId(@event.data.chara_info.scenario_id)
                                .TryGet(out var choice);
                            if (find)
                                tree.AddNode(MarkupText(choice.Effect, choice.State));
                            else
                                tree.AddNode((string.IsNullOrEmpty(originalChoice.FailedEffect) ? originalChoice.SuccessEffect : MarkupText(originalChoice.FailedEffect, 0)));
                        }
                        void AddNormalEvent()
                        {
                            //如果没有失败效果则显示成功效果（别问我为什么这么设置，问kamigame
                            if (string.IsNullOrEmpty(originalChoice.FailedEffect))
                                tree.AddNode(originalChoice.SuccessEffect);
                            else if (originalChoice.SuccessEffect == "未知效果" && originalChoice.FailedEffect == "未知效果")
                                tree.AddNode(MarkupText($"未知效果", int.MaxValue));
                            else
                                tree.AddNode(MarkupText($"(成功时){originalChoice.SuccessEffect}{Environment.NewLine}(失败时){originalChoice.FailedEffect}{Environment.NewLine}", -1));
                        }
                        string MarkupText(string text, int state)
                        {
                            return state switch
                            {
                                -1 => $"[darkorange on #081129]{text}(不知道是否成功)[/]", //未知的
                                0 => $"[#FF0050 on #081129]{text}[/]", //失败
                                1 => $"[mediumspringgreen on #081129]{text}[/]", //成功
                                2 => $"[lightgoldenrod1 on #081129]{text}[/]", //大成功
                                int.MaxValue => $"[#afafaf on #081129]{text}[/]" //中性
                            };
                        }
                    }
                    mainTree.AddNode(eventTree);
                    AnsiConsole.Write(mainTree);
                }
                else //未知事件，直接显示ChoiceIndex
                {
                    var mainTree = new Tree(Resource.SingleModeCheckEvent_Event_UnknownSource);
                    var eventTree = new Tree($"{Resource.SingleModeCheckEvent_Event_UnknownEvent}({i?.story_id})");
                    for (var j = 0; j < i?.event_contents_info?.choice_array.Length; ++j)
                    {
                        var tree = new Tree(string.Format(Resource.SingleModeCheckEvent_Event_UnknownOption, i.event_contents_info.choice_array[j].select_index));
                        tree.AddNode(Resource.SingleModeCheckEvent_Event_UnknownEffect);
                        eventTree.AddNode(tree);
                    }
                    mainTree.AddNode(eventTree);
                    AnsiConsole.Write(mainTree);
                }
            }
        }
    }
}
