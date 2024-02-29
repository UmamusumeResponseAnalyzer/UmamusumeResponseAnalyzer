using Gallop;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static UmamusumeResponseAnalyzer.Localization.Game;
using static UmamusumeResponseAnalyzer.LocalizedLayouts.Handlers.ParseTeamStadiumOpponentListResponse;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {

        public static void ParseTeamStadiumOpponentListResponse(Gallop.TeamStadiumOpponentListResponse @event)
        {
            var data = @event.data;
            foreach (var i in data.opponent_info_array.OrderByDescending(x => -x.strength))
            {
                var teamData = i.team_data_array.Where(x => x.trained_chara_id != 0).GroupBy(x => x.distance_type).ToDictionary(x => x.Key, x => x.ToList());
                var table = new Table();
                table.AddColumns(Enumerable.Repeat(new TableColumn(ColumnWidth).NoWrap(), 2 + teamData.Values.Sum(x => x.Count)).ToArray());
                table.HideHeaders();
                var properTypeLine = new List<string> { string.Empty };
                var properValueLine = new List<string> { I18N_Proper };
                var speedLine = new List<string> { I18N_Speed };
                var staminaLine = new List<string> { I18N_Stamina };
                var powerLine = new List<string> { I18N_Power };
                var gutsLine = new List<string> { I18N_Nuts };
                var wizLine = new List<string> { I18N_Wiz };
                int totalPower = 0;
                int totalWiz = 0;
                int charaNum = 0;
                foreach (var j in teamData)
                {
                    foreach (var k in j.Value)
                    {
                        var trainedChara = i.trained_chara_array.First(x => x.trained_chara_id == k.trained_chara_id);
                        var properType = string.Empty;
                        var properValue = string.Empty;
                        properType += (k.distance_type switch
                        {
                            5 => I18N_Dirt,
                            _ => I18N_Grass
                        });
                        properValue += (k.distance_type switch
                        {
                            5 => GetProper(trainedChara.proper_ground_dirt),
                            _ => GetProper(trainedChara.proper_ground_turf)
                        });
                        properValue += ' ';
                        properType += (k.distance_type switch
                        {
                            1 => I18N_Short,
                            2 => I18N_Mile,
                            3 => I18N_Middle,
                            4 => I18N_Long,
                            5 => I18N_Mile
                        });
                        properValue += (k.distance_type switch
                        {
                            1 => GetProper(trainedChara.proper_distance_short),
                            2 => GetProper(trainedChara.proper_distance_mile),
                            3 => GetProper(trainedChara.proper_distance_middle),
                            4 => GetProper(trainedChara.proper_distance_long),
                            5 => GetProper(trainedChara.proper_distance_mile)
                        });
                        properValue += ' ';
                        properType += (k.running_style switch
                        {
                            1 => I18N_Nige,
                            2 => I18N_Senko,
                            3 => I18N_Sashi,
                            4 => I18N_Oikomi
                        });
                        properValue += (k.running_style switch
                        {
                            1 => GetProper(trainedChara.proper_running_style_nige),
                            2 => GetProper(trainedChara.proper_running_style_senko),
                            3 => GetProper(trainedChara.proper_running_style_sashi),
                            4 => GetProper(trainedChara.proper_running_style_oikomi)
                        });
                        properTypeLine.Add(properType);
                        properValueLine.Add(properValue);

                        charaNum += 1;

                        speedLine.Add(trainedChara.speed.ToString());
                        staminaLine.Add(trainedChara.stamina.ToString());

                        if (trainedChara.power < 600)
                            powerLine.Add("[green]" + trainedChara.power.ToString() + "[/]");
                        else if (trainedChara.power < 800)
                            powerLine.Add("[aqua]" + trainedChara.power.ToString() + "[/]");
                        else
                            powerLine.Add(trainedChara.power.ToString());
                        totalPower += trainedChara.power;

                        gutsLine.Add(trainedChara.guts.ToString());

                        if (trainedChara.wiz > 1600)
                            wizLine.Add("[aqua]" + trainedChara.wiz.ToString() + "[/]");
                        else
                            wizLine.Add(trainedChara.wiz.ToString());
                        totalWiz += trainedChara.wiz;



                    }
                }
                table.AddRow(properTypeLine.Append("A v g").ToArray());
                table.AddRow(properValueLine.Append("/ / /").ToArray());

                table.AddRow(speedLine.Append(speedLine.Skip(1).Average(x => int.Parse(x)).ToString("F0")).ToArray());
                table.AddRow(staminaLine.Append(staminaLine.Skip(1).Average(x => int.Parse(x)).ToString("F0")).ToArray());
                table.AddRow(powerLine.Append((totalPower / charaNum).ToString("F0")).ToArray());
                table.AddRow(gutsLine.Append(gutsLine.Skip(1).Average(x => int.Parse(x)).ToString("F0")).ToArray());
                table.AddRow(wizLine.Append((totalWiz / charaNum).ToString("F0")).ToArray());

                AnsiConsole.Write(table);
            }

            //设置宽度，Windows的CMD在过小时无法正常显示竞技场对手属性，会死循环
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (Console.BufferWidth < MinimumConsoleWidth || Console.WindowWidth < MinimumConsoleWidth))
            {
                Console.BufferWidth = MinimumConsoleWidth;
                Console.SetWindowSize(Console.BufferWidth, Console.WindowHeight);
            }

            static string GetProper(int proper) => proper switch
            {
                1 => "G",
                2 => "F",
                3 => "E",
                4 => "D",
                5 => "C",
                6 => "B",
                7 => "A",
                8 => "S",
                _ => throw new NotImplementedException()
            };
        }
    }
}
