using Spectre.Console;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;
using static UmamusumeResponseAnalyzer.Localization.Handlers.ParseSingleModeCheckEventResponse;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseSingleModeCheckEventResponse(Gallop.SingleModeCheckEventResponse @event)
        {
            // 这时当前事件还没有生效，先显示上一个事件的收益
            EventLogger.Update(@event);

            foreach (var i in @event.data.unchecked_event_array)
            {
                if (GameStats.stats[GameStats.currentTurn] != null)
                {
                    if (i.story_id == 830137001)//第一次点击女神
                    {
                        GameStats.stats[GameStats.currentTurn].venus_isVenusCountConcerned = false;
                    }

                    if (i.story_id == 830137003)//女神三选一事件
                    {
                        GameStats.stats[GameStats.currentTurn].venus_venusEvent = true;
                    }

                    if (i.story_id == 400006112)//ss训练
                    {
                        GameStats.stats[GameStats.currentTurn].larc_playerChoiceSS = true;
                    }

                    if (i.story_id == 809043002)//佐岳启动
                    {
                        GameStats.stats[GameStats.currentTurn].larc_zuoyueEvent = 5;
                    }

                    if (i.story_id == 809043003)//佐岳充电
                    {
                        var suc = i.event_contents_info.choice_array[0].select_index;
                        var eventType = 0;
                        if (suc == 1)//加心情
                        {
                            eventType = 2;
                        }
                        else if (suc == 2)//不加心情
                        {
                            eventType = 1;
                        }

                        GameStats.stats[GameStats.currentTurn].larc_zuoyueEvent = eventType;
                    }
                    if (i.story_id == 400006115)//远征佐岳加pt
                    {
                        GameStats.stats[GameStats.currentTurn].larc_zuoyueEvent = 4;
                    }
                    if (i.story_id == 809044002) // 凉花出门
                    {
                        GameStats.stats[GameStats.currentTurn].uaf_friendEvent = 5;
                    }
                    if (i.story_id == 809044003) // 凉花加体力
                    {
                        GameStats.stats[GameStats.currentTurn].uaf_friendEvent = 1;
                    }
                }

                //收录在数据库中
                if (Database.Events.TryGetValue(i.story_id, out var story))
                {
                    var mainTree = new Tree(story.TriggerName.EscapeMarkup()); //触发者名称
                    var eventTree = new Tree($"{story.Name.EscapeMarkup()}({i.story_id})"); //事件名称
                    var eventHasManualChoice = (i.event_contents_info.choice_array.Length >= 2);   // 是否有手动选项。有选项的不能获取结果了
                    for (var j = 0; j < i.event_contents_info.choice_array.Length; ++j)
                    {
                        var originalChoice = new Choice();
                        if (story.Choices.Count < (j + 1))
                        {
                            originalChoice.Option = string.Format(I18N_UnknownOption, i.event_contents_info.choice_array[j].select_index);
                            originalChoice.SuccessEffect = I18N_UnknownEffect;
                            originalChoice.FailedEffect = I18N_UnknownEffect;
                        }
                        else
                        {
                            originalChoice = story.Choices[j][0]; //因为kamigame的事件无法直接根据SelectIndex区分成功与否，所以必然只会有一个Choice;
                        }
                        //显示选项
                        var tree = new Tree($"{(string.IsNullOrEmpty(originalChoice.Option) ? I18N_NoOption : originalChoice.Option)}{(Config.Get(Localization.Config.I18N_DisableSelectIndex) ? string.Empty : $" @ {i.event_contents_info.choice_array[j].select_index}")}".EscapeMarkup());
                        if (!Config.Get(Localization.Config.I18N_DisableSelectIndex) && Database.SuccessEvent.TryGetValue(i.story_id, out var successEvent) && successEvent.Choices.Length > j) //是可以成功的事件且已在数据库中
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
                                if (!eventHasManualChoice)  // 没有手动选项，还能看
                                    tree.AddNode(MarkupText(choice.Effect, choice.State));
                                else
                                    AddNormalEvent();
                            else
                                tree.AddNode((string.IsNullOrEmpty(originalChoice.FailedEffect) ? originalChoice.SuccessEffect : MarkupText(originalChoice.FailedEffect, 0)));
                        }
                        void AddNormalEvent()
                        {
                            //如果没有失败效果则显示成功效果（别问我为什么这么设置，问kamigame
                            if (string.IsNullOrEmpty(originalChoice.FailedEffect))
                                tree.AddNode(originalChoice.SuccessEffect);
                            else if (originalChoice.SuccessEffect == I18N_UnknownEffect && originalChoice.FailedEffect == I18N_UnknownEffect)
                                tree.AddNode(MarkupText(I18N_UnknownEffect, State.None));
                            else
                                tree.AddNode($"[mediumspringgreen on #081129]({I18N_WhenSuccess}){originalChoice.SuccessEffect}[/]{Environment.NewLine}[#FF0050 on #081129]({I18N_WhenFail}){originalChoice.FailedEffect}[/]{Environment.NewLine}");
                        }
                        string MarkupText(string text, State state)
                        {
                            return state switch
                            {
                                State.Unknown => $"[darkorange on #081129]{text}({I18N_Unknown})[/]", //未知的
                                State.Fail => $"[#FF0050 on #081129]{text}[/]", //失败
                                State.Success => $"[mediumspringgreen on #081129]{text}[/]", //成功
                                State.GreatSuccess => $"[lightgoldenrod1 on #081129]{text}[/]", //大成功
                                State.None => $"[#afafaf on #081129]{text}[/]", //中性
                                _ => throw new NotImplementedException()
                            };
                        }
                    }
                    mainTree.AddNode(eventTree);
                    AnsiConsole.Write(mainTree);
                }
                else //未知事件，直接显示ChoiceIndex
                {
                    var mainTree = new Tree(I18N_UnknownSource);
                    var eventTree = new Tree($"{I18N_UnknownEvent}({i?.story_id})");
                    for (var j = 0; j < i?.event_contents_info?.choice_array.Length; ++j)
                    {
                        var tree = new Tree(string.Format(I18N_UnknownOption, i.event_contents_info.choice_array[j].select_index));
                        tree.AddNode(I18N_UnknownEffect);
                        eventTree.AddNode(tree);
                    }
                    mainTree.AddNode(eventTree);
                    AnsiConsole.Write(mainTree);

                }
            }
        }
    }
}
