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
        public static void ParseTrainedCharaLoadResponse(byte[] buffer)
        {
            var @event = TryDeserialize<Gallop.TrainedCharaLoadResponse>(buffer);
            var data = @event?.data;
            if (@event != default && data?.trained_chara_array.Length > 0 && data.trained_chara_favorite_array.Length > 0)
            {
                var fav_ids = data.trained_chara_favorite_array.Select(x => x.trained_chara_id).ToList();
                var chara = data.trained_chara_array.Where(x => fav_ids.Contains(x.trained_chara_id));
                var win_saddle_result = new List<(string Name, int WinSaddle, int Score)>();
                foreach (var i in chara)
                    win_saddle_result.Add((Database.IdToName[i.card_id], i.win_saddle_id_array.Intersect(i.succession_chara_array[0].win_saddle_id_array).Count()
                    + i.win_saddle_id_array.Intersect(i.succession_chara_array[1].win_saddle_id_array).Count(), i.rank_score));
                win_saddle_result.Sort((a, b) => b.WinSaddle.CompareTo(a.WinSaddle));
                var table = new Table
                {
                    Border = TableBorder.Ascii
                };
                table.AddColumns("种马名", "胜鞍加成", "分数");
                foreach (var (Name, WinSaddle, Score) in win_saddle_result)
                    table.AddRow(Name.EscapeMarkup(), WinSaddle.ToString(), Score.ToString());
                AnsiConsole.Write(table);
            }
        }
    }
}
