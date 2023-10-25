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
        public static void ParseTrainedCharaLoadResponse(Gallop.TrainedCharaLoadResponse @event)
        {
            var g1_saddles = new[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 147, 148, 153, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 184 };
            var data = @event.data;
            var fav_ids = data.trained_chara_favorite_array.Where(x => x.icon_type != 0).Select(x => x.trained_chara_id).ToList();
            var chara = data.trained_chara_array.Where(x => fav_ids.Contains(x.trained_chara_id));
            var win_saddle_result = new List<(string Name, int WinSaddleBonus, string WinSaddleArray, int Score)>();
            foreach (var i in chara)
            {
                var charaWinSaddle = i.win_saddle_id_array.Intersect(g1_saddles);
                var parentWinSaddle_a = i.succession_chara_array[0].win_saddle_id_array.Intersect(g1_saddles);
                var parentWinSaddle_b = i.succession_chara_array[1].win_saddle_id_array.Intersect(g1_saddles);
                var win_saddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3
                    + charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;
                win_saddle_result.Add((Database.IdToName[i.card_id], win_saddle, string.Join(',', charaWinSaddle), i.rank_score));
            }
            win_saddle_result.Sort((a, b) => b.WinSaddleBonus.CompareTo(a.WinSaddleBonus));
            var table = new Table
            {
                Border = TableBorder.Ascii
            };
            table.AddColumns("种马名", "胜鞍加成", "胜鞍", "分数");
            foreach (var (Name, WinSaddleBonus, WinSaddleArray, Score) in win_saddle_result)
                table.AddRow(Name.EscapeMarkup(), WinSaddleBonus.ToString(), WinSaddleArray, Score.ToString());
            AnsiConsole.Write(table);
        }
    }
}
