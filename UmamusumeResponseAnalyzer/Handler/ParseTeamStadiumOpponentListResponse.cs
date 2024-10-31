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
using Newtonsoft.Json;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {

        public static void ParseTeamStadiumOpponentListResponse(Gallop.TeamStadiumOpponentListResponse @event)
        {
            
            var data = @event.data;
            if (data.opponent_info_array != null)
            {
                // 3个是选人之前。由于trained_chara_array无了，读取对手账号信息
                foreach (var i in data.opponent_info_array)
                {
                    var str = i.strength;
                    var user = i.user_info;
                    var name = user.name;
                    var play_count = user.single_mode_play_count;
                    var day_count = user.total_login_day_count;
                    AnsiConsole.MarkupLine($"#{str}: [cyan]{name}[/] 登陆日数 {day_count} 育成数 {play_count} ([cyan]{((double)play_count / day_count).ToString("N1")}[/]/日)");
                    AnsiConsole.MarkupLine("------");
                }
                AnsiConsole.MarkupLine("");
            }
            else if (data.opponent_info_copy != null)
            {
                // 1个是选人之后，仍能看到属性
                var team = data.opponent_info_copy.team_data_array;
                var trained = data.opponent_info_copy.trained_chara_array;
                var name = data.opponent_info_copy.user_info.name;
                var distStats = new Dictionary<string, int>();
                var groundStats = new Dictionary<string, int>();
                var styleStats = new Dictionary<string, int>();

                var teamData = team.Where(x => x.trained_chara_id != 0).GroupBy(x => x.distance_type).ToDictionary(x => x.Key, x => x.ToList());
                foreach (var j in teamData)
                {
                    foreach (var k in j.Value)
                    {
                        var trainedChara = trained.FirstOrDefault(x => x.trained_chara_id == k.trained_chara_id);
                        if (trainedChara == null) continue;

                        // 场地适性
                        // 判断是否泥地
                        var groundType = k.distance_type switch
                        {
                            5 => I18N_Dirt,
                            _ => I18N_Grass
                        };
                        var groundProper = k.distance_type switch
                        {
                            5 => GetProper(trainedChara.proper_ground_dirt),
                            _ => GetProper(trainedChara.proper_ground_turf)
                        };
                        // 距离适性
                        var distType = k.distance_type switch
                        {
                            1 => I18N_Short,
                            2 => I18N_Mile,
                            3 => I18N_Middle,
                            4 => I18N_Long,
                            5 => I18N_Mile
                        };
                        var distProper = (k.distance_type switch
                        {
                            1 => GetProper(trainedChara.proper_distance_short),
                            2 => GetProper(trainedChara.proper_distance_mile),
                            3 => GetProper(trainedChara.proper_distance_middle),
                            4 => GetProper(trainedChara.proper_distance_long),
                            5 => GetProper(trainedChara.proper_distance_mile)
                        });
                        // 跑法适性
                        var styleType = k.running_style switch
                        {
                            1 => I18N_Nige,
                            2 => I18N_Senko,
                            3 => I18N_Sashi,
                            4 => I18N_Oikomi
                        };
                        var styleProper = k.running_style switch
                        {
                            1 => GetProper(trainedChara.proper_running_style_nige),
                            2 => GetProper(trainedChara.proper_running_style_senko),
                            3 => GetProper(trainedChara.proper_running_style_sashi),
                            4 => GetProper(trainedChara.proper_running_style_oikomi)
                        };
                        // 统计
                        if (!distStats.ContainsKey(distProper)) distStats[distProper] = 0;
                        distStats[distProper] += 1;
                        if (!groundStats.ContainsKey(groundProper)) groundStats[groundProper] = 0;
                        groundStats[groundProper] += 1;
                        if (!styleStats.ContainsKey(styleProper)) styleStats[styleProper] = 0;
                        styleStats[styleProper] += 1;
                    }
                } // foreach
                AnsiConsole.MarkupLine($"当前对手: [cyan]{name}[/]");
                AnsiConsole.MarkupLine($"距离适性: [cyan]{JsonConvert.SerializeObject(distStats)}[/]");
                AnsiConsole.MarkupLine($"场地适性: {JsonConvert.SerializeObject(groundStats)}");
                AnsiConsole.MarkupLine($"跑法适性: {JsonConvert.SerializeObject(styleStats)}");
                AnsiConsole.MarkupLine($"----");
                AnsiConsole.MarkupLine($"");
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
