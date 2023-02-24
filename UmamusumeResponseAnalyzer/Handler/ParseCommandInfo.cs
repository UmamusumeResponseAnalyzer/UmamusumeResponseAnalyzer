using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {

        public static void ParseCommandInfo(Gallop.SingleModeCheckEventResponse @event)
        {
            if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0) || @event.data.race_start_info != null) return;
            var failureRate = new Dictionary<int, int>
                { //60x是合宿训练，10x是平时训练
                    {101,@event.data.home_info.command_info_array.Any(x => x.command_id == 601) ? @event.data.home_info.command_info_array.First(x => x.command_id == 601).failure_rate : @event.data.home_info.command_info_array.First(x => x.command_id == 101).failure_rate },
                    {105,@event.data.home_info.command_info_array.Any(x => x.command_id == 602) ? @event.data.home_info.command_info_array.First(x => x.command_id == 602).failure_rate : @event.data.home_info.command_info_array.First(x => x.command_id == 105).failure_rate },
                    {102,@event.data.home_info.command_info_array.Any(x => x.command_id == 603) ? @event.data.home_info.command_info_array.First(x => x.command_id == 603).failure_rate : @event.data.home_info.command_info_array.First(x => x.command_id == 102).failure_rate },
                    {103,@event.data.home_info.command_info_array.Any(x => x.command_id == 604) ? @event.data.home_info.command_info_array.First(x => x.command_id == 604).failure_rate : @event.data.home_info.command_info_array.First(x => x.command_id == 103).failure_rate },
                    {106,@event.data.home_info.command_info_array.Any(x => x.command_id == 605) ? @event.data.home_info.command_info_array.First(x => x.command_id == 605).failure_rate : @event.data.home_info.command_info_array.First(x => x.command_id == 106).failure_rate }
                };
            var table = new Table();
            //失败率＞16%就标红
            table.AddColumns(
                new TableColumn($"速({(failureRate[101] > 16 ? $"[red]{failureRate[101]}[/]" : failureRate[101])}%)").Width(15)
                , new TableColumn($"耐({(failureRate[105] > 16 ? $"[red]{failureRate[105]}[/]" : failureRate[105])}%)").Width(15)
                , new TableColumn($"力({(failureRate[102] > 16 ? $"[red]{failureRate[102]}[/]" : failureRate[102])}%)").Width(15)
                , new TableColumn($"根({(failureRate[103] > 16 ? $"[red]{failureRate[103]}[/]" : failureRate[103])}%)").Width(15)
                , new TableColumn($"智({(failureRate[106] > 16 ? $"[red]{failureRate[106]}[/]" : failureRate[106])}%)").Width(15));

            var supportCards = @event.data.chara_info.support_card_array.ToDictionary(x => x.position, x => x.support_card_id); //当前S卡卡组
            var commandInfo = new Dictionary<int, string[]>();
            foreach (var command in @event.data.home_info.command_info_array)
            {
                if (command.command_id != 101 && command.command_id != 105 && command.command_id != 102 && command.command_id != 103 && command.command_id != 106 &&
                    command.command_id != 601 && command.command_id != 602 && command.command_id != 603 && command.command_id != 604 && command.command_id != 605) continue;
                var tips = command.tips_event_partner_array.Intersect(command.training_partner_array); //红感叹号 || Hint
                commandInfo.Add(command.command_id, command.training_partner_array
                    .Select(partner =>
                    {
                        var name = Database.SupportIdToShortName[(partner >= 1 && partner <= 7) ? supportCards[partner] : partner].EscapeMarkup(); //partner是当前S卡卡组的index（1~6，7是啥？我忘了）或者charaId（10xx)
                        if (partner >= 1 && partner <= 7)
                        {
                            if (name.Contains("[友]")) //友人单独标绿
                                name = $"[green]{name}[/]";
                            if (@event.data.chara_info.evaluation_info_array.First(x => x.target_id == partner).evaluation < 80) //羁绊不满80，无法触发友情训练标黄
                                name = $"[yellow]{name}[/]";
                        }
                        return tips.Contains(partner) ? $"[red]![/]{name}" : name; //有Hint就加个红感叹号，和游戏内表现一样
                    }).ToArray());
            }
            if (!commandInfo.SelectMany(x => x.Value).Any()) return;
            for (var i = 0; i < 5; ++i)
            {
                var rows = new List<string>();
                foreach (var j in commandInfo)
                {
                    rows.Add(j.Value.Length > i ? IsShining(j.Key, j.Value[i]) : string.Empty);
                }
                table.AddRow(rows.ToArray());
            }

            if (@event.data.chara_info.scenario_id == 4 && @event.data.free_data_set.pick_up_item_info_array != null)
            {
                var freeDataSet = @event.data.free_data_set;
                var coinNum = freeDataSet.coin_num;
                var inventory = freeDataSet.user_item_info_array?.ToDictionary(x => x.item_id, x => x.num);
                var shouldPromoteTea = (inventory?.ContainsKey(2301) ?? false) ||  //包里或者商店里有加干劲的道具
                    (inventory?.ContainsKey(2302) ?? false) ||
                    freeDataSet.pick_up_item_info_array.Any(x => x.item_id == 2301) ||
                    freeDataSet.pick_up_item_info_array.Any(x => x.item_id == 2302);
                var currentTurn = @event.data.chara_info.turn;

                var rows = new List<List<string>> { new(), new(), new(), new(), new() };
                {
                    var k = 0;
                    foreach (var j in freeDataSet.pick_up_item_info_array
                        .Where(x => x.item_buy_num != 1)
                        .GroupBy(x => x.item_id))
                    {
                        if (k == 5) k = 0;
                        var name = Database.ClimaxItem[j.First().item_id];
                        if (name.Contains("+15") ||
                            name.Contains("体力+") ||
                            (name == "苦茶" && shouldPromoteTea) ||
                            name == "BBQ" ||
                            name == "切者" ||
                            name == "哨子" ||
                            name == "60%喇叭" ||
                            name == "御守" ||
                            name == "蹄铁・極"
                            )
                            name = $"[green]{name}[/]";
                        var itemCount = j.Count();
                        var remainTurn = j.First().limit_turn == 0 ? ((currentTurn - 1) / 6 + 1) * 6 + 1 - currentTurn : j.First().limit_turn + 1 - currentTurn;
                        var remainTurnRemind = $"{remainTurn}T";
                        if (remainTurn == 3)
                            remainTurnRemind = $"[green1]{remainTurnRemind}[/]";
                        else if (remainTurn == 2)
                            remainTurnRemind = $"[orange1]{remainTurnRemind}[/]";
                        else if (remainTurn == 1)
                            remainTurnRemind = $"[red]{remainTurnRemind}[/]";
                        rows[k].Add($"{name}:{itemCount}/{remainTurnRemind}");
                        k++;
                    }
                }
                for (var i = 0; i < 5; ++i)
                    table.Columns[i].Footer = new Rows(rows[i].Select(x => new Markup(x)));
            }
            if (@event.data.chara_info.scenario_id == 5 && @event.data.venus_data_set != null)
            {
                var spiritId = new Dictionary<int, string>
                {
                    { 1, "[red]速[/]" },
                    { 2, "[red]耐[/]" },
                    { 3, "[red]力[/]" },
                    { 4, "[red]根[/]" },
                    { 5, "[red]智[/]" },
                    { 6, "[red]星[/]" },
                    { 9, "[blue]速[/]" },
                    { 10,"[blue]耐[/]" },
                    { 11,"[blue]力[/]" },
                    { 12,"[blue]根[/]" },
                    { 13,"[blue]智[/]" },
                    { 14,"[blue]星[/]" },
                    { 17,"[yellow]速[/]" },
                    { 18,"[yellow]耐[/]" },
                    { 19,"[yellow]力[/]" },
                    { 20,"[yellow]根[/]" },
                    { 21,"[yellow]智[/]" },
                    { 22,"[yellow]星[/]" },
                };
                foreach (var i in @event.data.venus_data_set.venus_chara_command_info_array)
                {
                    switch (i.command_type)
                    {
                        case 1:
                            {
                                switch (i.command_id)
                                {
                                    case 101:
                                    case 601:
                                        table.Columns[0].Header = new Markup($"速({(failureRate[101] > 16 ? $"[red]{failureRate[101]}[/]" : failureRate[101])}%) | {spiritId[i.spirit_id]}{(i.is_boost == 1 ? "x2" : string.Empty)}"); break;
                                    case 102:
                                    case 603:
                                        table.Columns[2].Header = new Markup($"力({(failureRate[102] > 16 ? $"[red]{failureRate[102]}[/]" : failureRate[102])}%) | {spiritId[i.spirit_id]}{(i.is_boost == 1 ? "x2" : string.Empty)}"); break;
                                    case 103:
                                    case 604:
                                        table.Columns[3].Header = new Markup($"根({(failureRate[103] > 16 ? $"[red]{failureRate[103]}[/]" : failureRate[103])}%) | {spiritId[i.spirit_id]}{(i.is_boost == 1 ? "x2" : string.Empty)}"); break;
                                    case 105:
                                    case 602:
                                        table.Columns[1].Header = new Markup($"耐({(failureRate[105] > 16 ? $"[red]{failureRate[105]}[/]" : failureRate[105])}%) | {spiritId[i.spirit_id]}{(i.is_boost == 1 ? "x2" : string.Empty)}"); break;
                                    case 106:
                                    case 605:
                                        table.Columns[4].Header = new Markup($"智({(failureRate[106] > 16 ? $"[red]{failureRate[106]}[/]" : failureRate[106])}%) | {spiritId[i.spirit_id]}{(i.is_boost == 1 ? "x2" : string.Empty)}"); break;
                                }
                                break;
                            }
                        case 3:
                            {
                                table.Columns[0].Footer = new Rows(new Markup($"出行 | {spiritId[i.spirit_id]}{(i.is_boost == 1 ? "x2" : string.Empty)}"));
                                break;
                            }
                        case 4:
                            {
                                table.Columns[2].Footer = new Rows(new Markup($"比赛 | {spiritId[i.spirit_id]}{(i.is_boost == 1 ? "x2" : string.Empty)}"));
                                break;
                            }
                        case 7:
                            {
                                table.Columns[1].Footer = new Rows(new Markup($"休息 | {spiritId[i.spirit_id]}{(i.is_boost == 1 ? "x2" : string.Empty)}"));
                                break;
                            }
                    }
                }
            }
            AnsiConsole.Write(table);

            string IsShining(int commandId, string card)
            {
                //在得意位置上
                var shouldShining = card.Contains(commandId switch
                {
                    101 => "[速]",
                    105 => "[耐]",
                    102 => "[力]",
                    103 => "[根]",
                    106 => "[智]",
                    601 => "[速]",
                    602 => "[耐]",
                    603 => "[力]",
                    604 => "[根]",
                    605 => "[智]",
                });
                if (@event.data.chara_info.scenario_id == 5 && @event.data.venus_data_set.venus_spirit_active_effect_info_array.Any(x => x.chara_id == 9042 && x.effect_group_id == 421)
                    && (card.Contains("[速]") || card.Contains("[耐]") || card.Contains("[力]") || card.Contains("[根]") || card.Contains("[智]")))
                {
                    var redIndex = card.IndexOf("[red]![/]");
                    card = card.Insert(redIndex > -1 ? redIndex + 9 : 0, "[aqua]");
                    if (card.Contains("[yellow]"))
                        card = new Regex(Regex.Escape("[/]")).Replace(card.Replace("[yellow]", string.Empty), string.Empty, 1);
                    return $"{card}[/]";
                }
                return shouldShining ? $"[aqua]{card}[/]" : card;
            }
        }
    }
}
