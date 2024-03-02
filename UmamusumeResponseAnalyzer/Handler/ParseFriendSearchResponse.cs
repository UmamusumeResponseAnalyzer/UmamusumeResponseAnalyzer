using Spectre.Console;
using System.Text;
using static UmamusumeResponseAnalyzer.Localization.Handlers.ParseFriendSearchResponse;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseFriendSearchResponse(Gallop.FriendSearchResponse @event)
        {
            var data = @event.data;
            var chara = data.practice_partner_info;
            // 每个相同的重赏胜场加3胜鞍加成
            var charaWinSaddle = chara.win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_a = chara.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_b = chara.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
            var win_saddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3
                + charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;
            // 应用因子强化
            if (chara.factor_extend_array != null)
            {
                foreach (var i in chara.factor_extend_array)
                {
                    if (i.position_id == 1)
                    {
                        var extendedFactor = chara.factor_info_array.FirstOrDefault(x => x.factor_id == i.base_factor_id);
                        if (extendedFactor == default) continue;
                        extendedFactor.factor_id = i.factor_id;
                    }
                    else
                    {
                        var successionChara = chara.succession_chara_array.FirstOrDefault(x => x.position_id == i.position_id);
                        if (successionChara == default) continue;
                        var extendedFactor = successionChara.factor_info_array.FirstOrDefault(x => x.factor_id == i.base_factor_id);
                        if (extendedFactor == default) continue;
                        extendedFactor.factor_id = i.factor_id;
                    }
                }
            }

            AnsiConsole.Write(new Rule());
            AnsiConsole.WriteLine(I18N_Friend, data.user_info_summary.name, data.user_info_summary.viewer_id, data.follower_num);
            AnsiConsole.WriteLine(I18N_Uma, Database.Names.GetUmamusume(chara.card_id).FullName, win_saddle, chara.rank_score);
            AnsiConsole.WriteLine(I18N_WinSaddle, string.Join(',', charaWinSaddle));
            if (Database.SaddleNames.Count != 0)    // 这里WinSaddleDetail有编码问题？
                AnsiConsole.WriteLine(I18N_WinSaddleDetail.Trim(), string.Join(',', charaWinSaddle.Select(x => Database.SaddleNames[x])));
            var tree = new Tree(I18N_Factor);
            
            var max = chara.factor_info_array.Select(x => x.factor_id).Concat(chara.succession_chara_array[0].factor_info_array.Select(x => x.factor_id))
                .Concat(chara.succession_chara_array[1].factor_info_array.Select(x => x.factor_id))
                .Where((x, index) => index % 2 == 0)
                .Max(x => GetRenderWidth(Database.FactorIds[x]));
            var representative = AddFactors(I18N_UmaFactor, chara.factor_info_array.Select(x => x.factor_id).ToArray(), max);
            var inheritanceA = AddFactors(string.Format(I18N_ParentFactor, chara.succession_chara_array[0].owner_viewer_id), chara.succession_chara_array[0].factor_info_array.Select(x => x.factor_id).ToArray(), max);
            var inheritanceB = AddFactors(string.Format(I18N_ParentFactor, chara.succession_chara_array[1].owner_viewer_id), chara.succession_chara_array[1].factor_info_array.Select(x => x.factor_id).ToArray(), max);

            tree.AddNodes(representative, inheritanceA, inheritanceB);
            AnsiConsole.Write(tree);
            AnsiConsole.Write(new Rule());
        }
        public static void ParseFriendSearchResponseSimple(Gallop.FriendSearchResponse @event)
        {
            var data = @event.data;
            AnsiConsole.Write(new Rule());
            AnsiConsole.WriteLine(I18N_FriendSimple, data.user_info_summary.name, data.user_info_summary.viewer_id);
            AnsiConsole.WriteLine(I18N_UmaSimple, Database.Names.GetUmamusume(data.user_info_summary.user_trained_chara.card_id).FullName);
            var tree = new Tree(I18N_Factor);

            var i = data.user_info_summary.user_trained_chara;
            var max = i.factor_info_array.Select(x => x.factor_id)
                .Where((x, index) => index % 2 == 0)
                .Max(x => GetRenderWidth(Database.FactorIds[x]));
            var representative = AddFactors(I18N_UmaFactor, i.factor_info_array.Select(x => x.factor_id).ToArray(), max);

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
                var gap = 12 + max - GetRenderWidth(Database.FactorIds[even[index]]);
                if (gap < 0) { gap = 2; }
                sb.Append(string.Join(string.Empty, Enumerable.Repeat(' ', gap)));
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
                3 => $"[#FFFFFF on #37B8F4]{name}[/]", // 蓝
                4 => $"[#FFFFFF on #FF78B2]{name}[/]", // 红
                8 => $"[#794016 on #91D02E]{name}[/]", // 固有
                _ => $"[#794016 on #E1E2E1]{name}[/]", // 白
            };
        }
        static int GetRenderWidth(string text)
        {
            return text.Sum(x => x.GetCellWidth());
        }
    }
}
