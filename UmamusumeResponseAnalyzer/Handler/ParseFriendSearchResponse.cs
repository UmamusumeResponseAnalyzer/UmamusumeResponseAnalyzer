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
        public static void ParseFriendSearchResponse(Gallop.FriendSearchResponse @event)
        {
            var data = @event.data;
            var i = data.practice_partner_info;
            //每个相同的重赏胜场加1胜鞍加成
            var (Name, WinSaddle, Score) = (Database.IdToName?[i.card_id], i.win_saddle_id_array.Intersect(i.succession_chara_array[0].win_saddle_id_array).Count()
                    + i.win_saddle_id_array.Intersect(i.succession_chara_array[1].win_saddle_id_array).Count(), i.rank_score);
            AnsiConsole.Write(new Rule());
            AnsiConsole.WriteLine($"好友：{data.user_info_summary.name}\t\tID：{data.user_info_summary.viewer_id}\t\tFollower数：{data.follower_num}");
            AnsiConsole.WriteLine($"种马：{Name}\t\t{WinSaddle}\t\t{Score}");
            AnsiConsole.Write(new Rule());
        }
    }
}
