using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseTrainedCharaLoadResponse(Gallop.TrainedCharaLoadResponse @event)
        {
            var data = @event.data;
            var fav_ids = data.trained_chara_favorite_array.Select(x => x.trained_chara_id).ToList();
            var chara = data.trained_chara_array.Where(x => x.is_locked == 1 && fav_ids.Contains(x.trained_chara_id));
            var win_saddle_result = new List<(string Name, int WinSaddleBonus, string WinSaddleArray, int Score)>();
            foreach (var i in chara)
            {
                var charaWinSaddle = i.win_saddle_id_array.Intersect(Database.SaddleIds);
                var parentWinSaddle_a = i.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
                var parentWinSaddle_b = i.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
                var win_saddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3
                    + charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;
                win_saddle_result.Add((Database.Names[i.card_id], win_saddle, string.Join(',', charaWinSaddle), i.rank_score));
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
