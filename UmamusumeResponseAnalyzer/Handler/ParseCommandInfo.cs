using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {

        public static void ParseCommandInfo(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.SingleModeCheckEventResponse>(buffer);
            if (@event != default && @event.data.home_info != null && @event.data.home_info.command_info_array != null && @event.data.home_info.command_info_array.Length != 0)
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
                    $"速({(failureRate[101] > 16 ? $"[red]{failureRate[101]}[/]" : failureRate[101])}%)"
                    , $"耐({(failureRate[105] > 16 ? $"[red]{failureRate[105]}[/]" : failureRate[105])}%)"
                    , $"力({(failureRate[102] > 16 ? $"[red]{failureRate[102]}[/]" : failureRate[102])}%)"
                    , $"根({(failureRate[103] > 16 ? $"[red]{failureRate[103]}[/]" : failureRate[103])}%)"
                    , $"智({(failureRate[106] > 16 ? $"[red]{failureRate[106]}[/]" : failureRate[106])}%)");

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
                AnsiConsole.Write(table);

                //这里其实并无法判断是否触发友情训练（彩圈），只会判定在他是否在该在的位置上。
                //但是因为aqua标签会被羁绊不满80时的yellow覆盖，所以也只会在有彩圈的时候才显示为aqua，实际效果是正确的。
                static string IsShining(int commandId, string card)
                {
                    return card.Contains(commandId switch
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
                    }) ? $"[aqua]{card}[/]" : card;
                }
            }
        }
    }
}
