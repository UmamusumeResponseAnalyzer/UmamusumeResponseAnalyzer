using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Localization;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {

        public static void ParseSkillTipsResponse(Gallop.SingleModeCheckEventResponse @event)
        {
            var totalSP = @event.data.chara_info.skill_point;
            var tips = @event.data.chara_info.skill_tips_array
                .Select(x => Database.Skills[(x.group_id, x.rarity)].Select(y => y.Apply(@event.data.chara_info, x.level)))
                .SelectMany(x => x)
                .Where(x => x.Rate > 0 && !@event.data.chara_info.skill_array.Any(y => y.skill_id == x.Id))
                .ToList();
            foreach (var i in Database.TalentSkill[@event.data.chara_info.card_id])
            {
                if (!tips.Any(x => x.Id == i.SkillId) && !@event.data.chara_info.skill_array.Any(y => y.skill_id == i.SkillId))
                {
                    tips.Add(Database.Skills[i.SkillId].Apply(@event.data.chara_info, 0));
                }
            }
            foreach (var i in tips.GroupBy(x => x.GroupId))
            {
                if (i.Count() > 1)
                {
                    foreach (var j in i.OrderByDescending(x => x.Rarity).ThenByDescending(x => x.Rate))
                    {
                        j.TotalCost = j.Cost + i.Where(x => x.Id != j.Id).Sum(x => x.Cost);
                    }
                }
            }
            var learn = new List<SkillManager.SkillData>();
            do
            {
                if (learn.Any()) { learn.Clear(); totalSP = @event.data.chara_info.skill_point; }
                int[][] Matrix = new int[tips.Count][];
                int[][] Picks = new int[tips.Count][];
                for (var i = 0; i < Matrix.Length; i++) { Matrix[i] = new int[totalSP + 1]; }
                for (var i = 0; i < Picks.Length; i++) { Picks[i] = new int[totalSP + 1]; }
                Recursive(tips.Count - 1, totalSP, 1);
                for (var i = tips.Count - 1; i >= 0 && totalSP >= 0; --i)
                {
                    if (Picks[i][totalSP] == 1)
                    {
                        totalSP -= tips[i].TotalCost;
                        learn.Add(tips[i]);
                    }
                }

                //如果上位和下位技能同时学习，则删除下位技能后重新计算，偷鸡做法。
                foreach (var i in learn.GroupBy(x => x.GroupId).Where(x => x.Count() > 1))
                {
                    var duplicated = learn.Where(x => x.GroupId == i.Key);
                    foreach (var j in duplicated)
                    {
                        var super = learn.FirstOrDefault(x => x.GroupId == j.GroupId && (x.Rarity < j.Rarity || x.Rate < j.Rate));
                        if (super != default)
                        {
                            tips.Remove(super);
                        }
                    }
                }

                // 0/1 knapsack problem
                int Recursive(int i, int w, int depth)
                {
                    var take = 0;
                    if (Matrix[i][w] != 0) { return Matrix[i][w]; }

                    if (i == 0)
                    {
                        if (tips[i].TotalCost <= w)
                        {
                            Picks[i][w] = 1;
                            Matrix[i][w] = tips[0].Grade;
                            return tips[i].Grade;
                        }

                        Picks[i][w] = -1;
                        Matrix[i][w] = 0;
                        return 0;
                    }

                    if (tips[i].TotalCost <= w)
                    {
                        take = tips[i].Grade + Recursive(i - 1, w - tips[i].TotalCost, depth + 1);
                    }

                    var dontTake = Recursive(i - 1, w, depth + 1);

                    Matrix[i][w] = Math.Max(take, dontTake);
                    if (take > dontTake)
                    {
                        Picks[i][w] = 1;
                    }
                    else
                    {
                        Picks[i][w] = -1;
                    }

                    return Matrix[i][w];
                }
            } while (learn.GroupBy(x => x.GroupId).Any(x => x.Count() > 1));
            learn = learn.OrderBy(x => x.DisplayOrder).ToList();

            var table = new Table();
            table.Title(string.Format(Resource.MaximiumGradeSkillRecommendation_Title, @event.data.chara_info.skill_point, @event.data.chara_info.skill_point - totalSP, totalSP));
            table.AddColumns(Resource.MaximiumGradeSkillRecommendation_Columns_SkillName, Resource.MaximiumGradeSkillRecommendation_Columns_RequireSP, Resource.MaximiumGradeSkillRecommendation_Columns_Grade);
            table.Columns[0].Centered();
            foreach (var i in learn)
            {
                table.AddRow($"{i.Name}", $"{i.TotalCost}", $"{i.Grade}");
            }
            var statusPoint = Database.StatusToPoint[@event.data.chara_info.speed]
                            + Database.StatusToPoint[@event.data.chara_info.stamina]
                            + Database.StatusToPoint[@event.data.chara_info.power]
                            + Database.StatusToPoint[@event.data.chara_info.guts]
                            + Database.StatusToPoint[@event.data.chara_info.wiz];
            var previousLearnPoint = 0; //之前学的技能的累计评价点
            foreach (var i in @event.data.chara_info.skill_array)
            {
                if (i.skill_id >= 1000000) continue; // Carnival bonus
                if (i.skill_id.ToString()[..1] == "1" && i.skill_id > 100000) //3*固有
                {
                    previousLearnPoint += 170 * i.level;
                }
                else if (i.skill_id.ToString().Length == 5) //2*固有
                {
                    previousLearnPoint += 120 * i.level;
                }
                else
                {
                    var (GroupId, Rarity, Rate) = Database.Skills.Deconstruction(i.skill_id);
                    if (!learn.Any(x => x.GroupId == GroupId))
                        previousLearnPoint += Database.Skills[i.skill_id] == null ? 0 : Database.Skills[i.skill_id].Apply(@event.data.chara_info, 0).Grade;
                }
            }
            var totalPoint = learn.Sum(x => x.Grade) + previousLearnPoint + statusPoint;
            table.Caption(string.Format(Resource.MaximiumGradeSkillRecommendation_Caption, previousLearnPoint, learn.Sum(x => x.Grade), statusPoint, totalPoint, Database.GradeToRank.First(x => x.Min < totalPoint && totalPoint < x.Max).Rank));
            AnsiConsole.Write(table);
        }
    }
}
