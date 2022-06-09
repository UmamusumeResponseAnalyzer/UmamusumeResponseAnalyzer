using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Localization;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseSkillTipsResponse(Gallop.SingleModeCheckEventResponse @event)
        {
            var totalSP = @event.data.chara_info.skill_point;
            //翻译技能tips方便使用
            var tips = @event.data.chara_info.skill_tips_array
                .Select(x => Database.Skills[(x.group_id, x.rarity)].Select(y => y.Apply(@event.data.chara_info, x.level)))
                .SelectMany(x => x)
                .Where(x => x.Rate > 0 && !@event.data.chara_info.skill_array.Any(y => y.skill_id == x.Id))
                .ToList();
            //添加天赋技能
            foreach (var i in Database.TalentSkill[@event.data.chara_info.card_id].Where(x => x.Rank <= @event.data.chara_info.talent_level))
            {
                if (!tips.Any(x => x.Id == i.SkillId) && !@event.data.chara_info.skill_array.Any(y => y.skill_id == i.SkillId))
                {
                    tips.Add(Database.Skills[i.SkillId].Apply(@event.data.chara_info));
                }
            }
            //添加上位技能缺少的下位技能（为方便计算切者技能点）
            foreach (var group in tips.GroupBy(x => x.GroupId))
            {
                var skills = Database.Skills.GetAllByGroupId(group.Key)
                    .Where(x => x.Rarity < group.Max(y => y.Rarity) || x.Rate < group.Max(y => y.Rate))
                    .Where(x => x.Rate > 0);
                var ids = skills.ExceptBy(tips.Select(x => x.Id), x => x.Id);
                foreach (var i in ids)
                {
                    tips.Add(i.Apply(@event.data.chara_info, 0));
                }
            }
            //纠正技能总花费
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

            var learn = new List<SkillData>();
            // 保证技能列表中的列表都是最上位技能（有下位技能则去除）
            // 理想中tips里应只保留最上位技能，其所有的下位技能都去除
            var inferiors = tips
                    .SelectMany(x => Database.Skills.GetAllByGroupId(x.GroupId))
                    .DistinctBy(x => x.Id)
                    .OrderByDescending(x => x.Rarity)
                    .ThenByDescending(x => x.Rate)
                    .GroupBy(x => x.GroupId)
                    .Where(x => x.Count() > 1)
                    .SelectMany(x => tips.Where(y => y.GroupId == x.Key)
                        .OrderByDescending(y => y.Rarity)
                        .ThenByDescending(y => y.Rate)
                        .Skip(1) //跳过当前有的最高级的hint
                        .Select(y => y.Id));
            tips.RemoveAll(x => inferiors.Contains(x.Id)); //只保留最上位技能，下位技能去除

            // 01背包变种
            var dp = new int[totalSP + 1];
            var dpLog = Enumerable.Range(0, totalSP + 1).Select(x => new List<int>()).ToList(); // 记录dp时所选的技能，存技能Id

            for (int i = 0; i < tips.Count; i++)
            {
                var s = tips[i];
                // 读取此技能可以点的所有情况
                int[] SuperiorCost = { int.MaxValue, int.MaxValue };
                int[] SuperiorGrade = { int.MinValue, int.MinValue };
                if (s.Inferior != null)
                {
                    // 绝大多数金技能
                    SuperiorCost[0] = s.TotalCost;
                    SuperiorGrade[0] = s.Grade;
                    s = s.Inferior.Apply(@event.data.chara_info);
                    if (s.Inferior != null)
                    {
                        // 绝大多数金绿技能
                        SuperiorCost[1] = SuperiorCost[0];
                        SuperiorGrade[1] = SuperiorGrade[0];
                        SuperiorCost[0] = s.TotalCost;
                        SuperiorGrade[0] = s.Grade;
                        s = s.Inferior.Apply(@event.data.chara_info);
                    }
                    // 退化技能到最低级，方便选择
                }

                for (int j = totalSP; j >= s.Cost; j--)
                {
                    // 背包四种选法
                    // 0-不选
                    // 1-只选此技能
                    // 2-选这个技能和它的上一级技能
                    // 3-选这个技能的最高位技（全点）
                    int[] choice =
                    {
                        dp[j],
                        dp[j - s.Cost] + s.Grade,

                        j  - SuperiorCost[0] >= 0 ?
                            dp[j - SuperiorCost[0]] + SuperiorGrade[0] :
                            -1,

                        j  - SuperiorCost[1] >= 0 ?
                            dp[j - SuperiorCost[1]] + SuperiorGrade[1] :
                            -1
                    };
                    // 判断是否为四种选法中的最优选择
                    if (IsBestOption(0))
                    {
                        dp[j] = choice[0];
                    }
                    else if (IsBestOption(1))
                    {
                        dp[j] = choice[1];
                        dpLog[j] = new(dpLog[j - s.Cost])
                        {
                            s.Id
                        };
                    }
                    else if (IsBestOption(2))
                    {
                        dp[j] = choice[2];
                        dpLog[j] = new(dpLog[j - SuperiorCost[0]])
                        {
                            s.Superior.Id
                        };
                    }
                    else if (IsBestOption(3))
                    {
                        dp[j] = choice[3];
                        dpLog[j] = new(dpLog[j - SuperiorCost[1]])
                        {
                            s.Superior.Superior.Id
                        };
                    }

                    bool IsBestOption(int index)
                    {
                        bool IsBest = true;
                        for (int k = 0; k < 4; k++)
                            IsBest = choice[index] >= choice[k] && IsBest;
                        return IsBest;
                    };
                }
            }

            // 读取最终选择的技能
            var learnSkillId = dpLog[totalSP];
            foreach (var id in learnSkillId)
            {
                foreach (var skill in tips)
                {
                    var inferior = skill.Inferior?.Apply(@event.data.chara_info);
                    var inferiorest = inferior?.Inferior?.Apply(@event.data.chara_info);
                    if (skill.Id == id)
                    {
                        learn.Add(skill);
                        totalSP -= skill.TotalCost;
                        continue;
                    }
                    else if (inferior != null && inferior.Id == id)
                    {
                        learn.Add(inferior);
                        totalSP -= inferior.TotalCost;
                        continue;
                    }
                    else if (inferiorest != null && inferiorest.Id == id)
                    {
                        learn.Add(inferiorest);
                        totalSP -= inferiorest.TotalCost;
                    }
                }
            }
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
