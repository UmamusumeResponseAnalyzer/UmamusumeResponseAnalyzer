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
                if (Database.Events.ContainsKey(i.story_id))
                {
                    var mainTree = new Tree(Database.Events[i.story_id].TriggerName.EscapeMarkup()); //触发者名称
                    var eventTree = new Tree(Database.Events[i.story_id].Name.EscapeMarkup()); //事件名称
                    for (var j = 0; j < i?.event_contents_info?.choice_array.Length; ++j)
                    {
                        //显示选项
                        var tree = new Tree($"{(string.IsNullOrEmpty(Database.Events[i.story_id].Choices[j].Option) ? Resource.SingleModeCheckEvent_Event_NoOption : Database.Events[i.story_id].Choices[j].Option)} @ {i.event_contents_info.choice_array[j].select_index}".EscapeMarkup());
                        if (Database.SuccessEvent.TryGetValue(Database.Events[i.story_id].Name, out var successEvent)) //是可以成功的事件且已在数据库中
                            AddSuccessEvent(successEvent.Choices.Where(x => x.ChoiceIndex == j + 1));
                        else
                            AddNormalEvent();
                        eventTree.AddNode(tree);

                        void AddSuccessEvent(IEnumerable<SuccessChoice> successChoices)
                        {
                            if (!successChoices.Any()) //如果ChoiceIndex没有记录在数据库中，即事件未被标记为成功，则改为添加失败事件
                            {
                                AddNormalEvent();
                                return;
                            }
                            var successChoice = successChoices.FirstOrDefault(x => x.SelectIndex == i.event_contents_info.choice_array[j].select_index);
                            if (successChoice != default && successChoice.Effects.ContainsKey(@event.data.chara_info.scenario_id))
                                tree.AddNode($"[mediumspringgreen on #081129]{(string.IsNullOrEmpty(successChoice.Effects[@event.data.chara_info.scenario_id]) ? Database.Events[i.story_id].Choices[j].SuccessEffect : successChoice.Effects[@event.data.chara_info.scenario_id]).EscapeMarkup()}[/]");
                            else if (string.IsNullOrEmpty(Database.Events[i.story_id].Choices[j].FailedEffect) || Database.Events[i.story_id].Choices[j].FailedEffect == "-")
                                tree.AddNode($"{Database.Events[i.story_id].Choices[j].SuccessEffect}".EscapeMarkup());
                            else
                                tree.AddNode($"[#FF0050 on #081129]{Database.Events[i.story_id].Choices[j].FailedEffect.EscapeMarkup()}[/]");
                        }
                        void AddNormalEvent()
                        {
                            //如果没有失败效果则显示成功效果（别问我为什么这么设置，问kamigame
                            if (string.IsNullOrEmpty(Database.Events[i.story_id].Choices[j].FailedEffect) || Database.Events[i.story_id].Choices[j].FailedEffect == "-")
                                tree.AddNode($"{Database.Events[i.story_id].Choices[j].SuccessEffect}".EscapeMarkup());
                            else
                                tree.AddNode($"{Database.Events[i.story_id].Choices[j].FailedEffect}".EscapeMarkup());
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
