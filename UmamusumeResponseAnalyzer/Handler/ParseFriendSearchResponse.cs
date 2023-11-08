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
        public static void ParseFriendSearchResponse(Gallop.FriendSearchResponse @event)
        {
            var data = @event.data;
            var chara = data.practice_partner_info;
            //每个相同的重赏胜场加3胜鞍加成
            var charaWinSaddle = chara.win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_a = chara.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_b = chara.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
            var win_saddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3
                + charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;

            AnsiConsole.Write(new Rule());
            AnsiConsole.WriteLine($"好友：{data.user_info_summary.name}\tID：{data.user_info_summary.viewer_id}\t\tFollower数：{data.follower_num}");
            AnsiConsole.WriteLine($"种马：{Database.Names[chara.card_id].Cast<UmaName>().FullName}\t胜鞍：{win_saddle}\t\t评分：{chara.rank_score}");
            AnsiConsole.WriteLine($"胜鞍列表：{string.Join(',', charaWinSaddle)}");
            if (Database.SaddleNames.Any())
                AnsiConsole.WriteLine($"胜鞍详细：{string.Join(',', charaWinSaddle.Select(x => Database.SaddleNames[x]))}{Environment.NewLine}");
            var tree = new Tree("因子");

            var max = chara.factor_info_array.Select(x => x.factor_id).Concat(chara.succession_chara_array[0].factor_info_array.Select(x => x.factor_id))
                .Concat(chara.succession_chara_array[1].factor_info_array.Select(x => x.factor_id))
                .Where((x, index) => index % 2 == 0)
                .Max(x => GetRenderWidth(Database.FactorIds[x]));
            var representative = AddFactors("代表", chara.factor_info_array.Select(x => x.factor_id).ToArray(), max);
            var inheritanceA = AddFactors($"祖辈@{chara.succession_chara_array[0].owner_viewer_id}", chara.succession_chara_array[0].factor_info_array.Select(x => x.factor_id).ToArray(), max);
            var inheritanceB = AddFactors($"祖辈@{chara.succession_chara_array[1].owner_viewer_id}", chara.succession_chara_array[1].factor_info_array.Select(x => x.factor_id).ToArray(), max);

            tree.AddNodes(representative, inheritanceA, inheritanceB);
            AnsiConsole.Write(tree);
            AnsiConsole.Write(new Rule());
        }
        public static void ParseFriendSearchResponseSimple(Gallop.FriendSearchResponse @event)
        {
            var data = @event.data;
            AnsiConsole.Write(new Rule());
            AnsiConsole.WriteLine($"好友：{data.user_info_summary.name}\t\tID：{data.user_info_summary.viewer_id}");
            AnsiConsole.WriteLine($"种马：{Database.Names[data.user_info_summary.user_trained_chara.card_id].Cast<UmaName>().FullName}");
            var tree = new Tree("因子");

            var i = data.user_info_summary.user_trained_chara;
            var max = i.factor_info_array.Select(x => x.factor_id)
                .Where((x, index) => index % 2 == 0)
                .Max(x => GetRenderWidth(Database.FactorIds[x]));
            var representative = AddFactors("代表", i.factor_info_array.Select(x => x.factor_id).ToArray(), max);

            tree.AddNodes(representative);
            AnsiConsole.Write(tree);
            AnsiConsole.Write(new Rule());
        }
        static Tree AddFactors(string title, int[] id_array, int max)
        {
            var tree = new Tree(title);
            var ordered = id_array.Take(2).Append(id_array[^1]).Concat(id_array.Skip(2).SkipLast(1));
            var even = ordered.Where((x, index) => index % 2 == 0).ToArray();
            var odd = ordered.Where((x, index) => index % 2 != 0).ToArray();
            foreach (var index in Enumerable.Range(0, even.Length))
            {
                var sb = new StringBuilder();
                sb.Append(FactorName(even[index]));
                sb.Append(string.Join(string.Empty, Enumerable.Repeat(' ', 12 + max - GetRenderWidth(Database.FactorIds[even[index]]))));
                sb.Append(odd.Length > index ? FactorName(odd[index]) : "");
                tree.AddNode(sb.ToString());
            }
            return tree;
        }
        static string FactorName(int factorId)
        {
            var name = Database.FactorIds[factorId];
            return factorId.ToString().Length switch
            {
                3 => $"[white on #37B8F4]{name}[/]",
                4 => $"[white on #FF78B2]{name}[/]",
                8 => $"[white on #91D02E]{name}[/]",
                _ => $"[#794016 on #E1E2E1]{name}[/]",
            };
        }
        static int GetRenderWidth(string text)
        {
            return text.Sum(x => x.GetCellWidth());
        }
    }
}
