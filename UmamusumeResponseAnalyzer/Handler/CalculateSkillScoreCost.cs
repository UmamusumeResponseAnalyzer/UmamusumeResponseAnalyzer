using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Entities;
using static UmamusumeResponseAnalyzer.Localization.Handlers.ParseSkillTipsResponse;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        //按技能性价比排序
        public static List<SkillData> CalculateSkillScoreCost(Gallop.SingleModeCheckEventResponse @event, SkillManager skills, bool removeInferiors)
        {
            var hasUnknownSkills = false;
            var totalSP = @event.data.chara_info.skill_point;
            var tipsRaw = @event.data.chara_info.skill_tips_array;
            var tipsExistInDatabase = tipsRaw.Where(x => skills[(x.group_id, x.rarity)] != null);//去掉数据库中没有的技能，避免报错
            var tipsNotExistInDatabase = tipsRaw.Where(x => skills[(x.group_id, x.rarity)] == null);//数据库中没有的技能
            foreach (var i in tipsNotExistInDatabase)
            {
                hasUnknownSkills = true;
                var lineToPrint = string.Format(I18N_UnknownSkillAlert, i.group_id, i.rarity);
                for (var rarity = 0; rarity < 10; rarity++)
                {
                    var maybeInferiorSkills = skills[(i.group_id, rarity)];
                    if (maybeInferiorSkills != null)
                    {
                        foreach (var inferiorSkill in maybeInferiorSkills)
                        {
                            lineToPrint += string.Format(I18N_UnknownSkillSuperiorSuppose, inferiorSkill.Name);
                        }
                    }
                }
                AnsiConsole.MarkupLine($"[red]{lineToPrint}[/]");
            }
            //翻译技能tips方便使用
            var tips = tipsExistInDatabase
                .SelectMany(x => skills[(x.group_id, x.rarity)])
                .Where(x => x.Rate > 0)
                .ToList();
            //添加天赋技能
            bool unknownUma = false;//新出的马娘的天赋技能不在数据库中
            if (Database.TalentSkill.TryGetValue(@event.data.chara_info.card_id, out TalentSkillData[]? value))
            {
                foreach (var i in value.Where(x => x.Rank <= @event.data.chara_info.talent_level))
                {
                    if (!tips.Any(x => x.Id == i.SkillId) && !@event.data.chara_info.skill_array.Any(y => y.skill_id == i.SkillId))
                    {
                        tips.Add(skills[i.SkillId]);
                    }
                }
            }
            else
            {
                unknownUma = true;
            }
            //添加上位技能缺少的下位技能（为方便计算切者技能点）
            foreach (var group in tips.GroupBy(x => x.GroupId))
            {
                var additionalSkills = skills.GetAllByGroupId(group.Key)
                    .Where(x => x.Rarity < group.Max(y => y.Rarity) || x.Rate < group.Max(y => y.Rate))
                    .Where(x => x.Rate > 0);
                var ids = additionalSkills.ExceptBy(tips.Select(x => x.Id), x => x.Id);
                tips.AddRange(ids);
            }

            if (removeInferiors)
            {
                // 保证技能列表中的列表都是最上位技能（有下位技能则去除）
                // 理想中tips里应只保留最上位技能，其所有的下位技能都去除
                var inferiors = tips
                        .SelectMany(x => skills.GetAllByGroupId(x.GroupId))
                        .DistinctBy(x => x.Id)
                        .OrderByDescending(x => x.Rarity)
                        .ThenByDescending(x => x.Rate)
                        .GroupBy(x => x.GroupId)
                        .Where(x => x.Any())
                        .SelectMany(x => tips.Where(y => y.GroupId == x.Key)
                            .OrderByDescending(y => y.Rarity)
                            .ThenByDescending(y => y.Rate)
                            .Skip(1) //跳过当前有的最高级的hint
                            .Select(y => y.Id));
                tips.RemoveAll(x => inferiors.Contains(x.Id)); //只保留最上位技能，下位技能去除
            }

            //把已买技能和它们的下位去掉
            foreach (var i in @event.data.chara_info.skill_array)
            {
                if (i.skill_id > 1000000 && i.skill_id < 2000000) continue; // 嘉年华&LoH技能
                var skill = skills[i.skill_id];
                if (skill == null)
                {
                    hasUnknownSkills = true;
                    AnsiConsole.MarkupLine(I18N_UnknownBoughtSkillAlert, i.skill_id);
                    continue;
                }
                skill.Cost = int.MaxValue;
                if (skill.Inferior != null)
                {
                    do
                    {
                        skill = skill.Inferior;
                        skill.Cost = int.MaxValue;
                    } while (skill.Inferior != null);
                }
            }

            if (unknownUma)
            {
                AnsiConsole.MarkupLine(I18N_UnknownUma, @event.data.chara_info.card_id);
            }
            if (hasUnknownSkills)
            {
                AnsiConsole.MarkupLine(I18N_UnknownSkillExistAlert);
            }
            return tips;
        }

        public static (List<SkillData>, int[]) DP(List<SkillData> tips, ref int totalSP, Gallop.SingleModeChara chara_info)
        {
            var learn = new List<SkillData>();
            // 01背包变种
            var dp = new int[totalSP + 101]; //多计算100pt，用于计算“边际性价比”
            var dpLog = Enumerable.Range(0, totalSP + 101).Select(x => new List<int>()).ToList(); // 记录dp时所选的技能，存技能Id
            for (int i = 0; i < tips.Count; i++)
            {
                var s = tips[i];
                // 读取此技能可以点的所有情况
                int[] SuperiorId = [0, 0, 0];
                int[] SuperiorCost = [int.MaxValue, int.MaxValue, int.MaxValue];
                int[] SuperiorGrade = [int.MinValue, int.MinValue, int.MinValue];

                SuperiorId[0] = s.Id;
                SuperiorCost[0] = s.Cost;
                SuperiorGrade[0] = s.Grade;

                if (SuperiorCost[0] != 0 && s.Inferior != null)
                {
                    s = s.Inferior;
                    SuperiorId[1] = s.Id;
                    SuperiorCost[1] = s.Cost;
                    SuperiorGrade[1] = s.Grade;
                    if (SuperiorCost[1] != 0 && s.Inferior != null)
                    {
                        s = s.Inferior;
                        SuperiorId[2] = s.Id;
                        SuperiorCost[2] = s.Cost;
                        SuperiorGrade[2] = s.Grade;
                    }
                }

                if (SuperiorGrade[0] == 0)
                    SuperiorCost[0] = int.MaxValue;
                if (SuperiorGrade[1] == 0)
                    SuperiorCost[1] = int.MaxValue;
                if (SuperiorGrade[2] == 0)
                    SuperiorCost[2] = int.MaxValue;

                // 退化技能到最低级，方便选择



                for (int j = totalSP + 100; j >= 0; j--)
                {
                    // 背包四种选法
                    // 0-不选
                    // 1-只选此技能
                    // 2-选这个技能和它的上一级技能
                    // 3-选这个技能的最高位技（全点）
                    var choice = new int[4];
                    choice[0] = dp[j];
                    choice[1] = j - SuperiorCost[0] >= 0 ?
                        dp[j - SuperiorCost[0]] + SuperiorGrade[0] :
                        -1;
                    choice[2] = j - SuperiorCost[1] >= 0 ?
                        dp[j - SuperiorCost[1]] + SuperiorGrade[1] :
                        -1;
                    choice[3] =
                        j - SuperiorCost[2] >= 0 ?
                        dp[j - SuperiorCost[2]] + SuperiorGrade[2] :
                        -1;
                    // 判断是否为四种选法中的最优选择
                    if (IsBestOption(0))
                    {
                        dp[j] = choice[0];
                    }
                    else if (IsBestOption(1))
                    {
                        dp[j] = choice[1];
                        dpLog[j] = new(dpLog[j - SuperiorCost[0]])
                        {
                            SuperiorId[0]
                        };
                    }
                    else if (IsBestOption(2))
                    {
                        dp[j] = choice[2];
                        dpLog[j] = new(dpLog[j - SuperiorCost[1]])
                        {
                            SuperiorId[1]
                        };
                    }
                    else if (IsBestOption(3))
                    {
                        dp[j] = choice[3];
                        dpLog[j] = new(dpLog[j - SuperiorCost[2]])
                        {
                            SuperiorId[2]
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
                    var inferior = skill.Inferior;
                    var inferiorest = inferior?.Inferior;
                    if (skill.Id == id)
                    {
                        learn.Add(skill);
                        totalSP -= skill.Cost;
                        continue;
                    }
                    else if (inferior != null && inferior.Id == id)
                    {
                        learn.Add(inferior);
                        totalSP -= inferior.Cost;
                        continue;
                    }
                    else if (inferiorest != null && inferiorest.Id == id)
                    {
                        learn.Add(inferiorest);
                        totalSP -= inferiorest.Cost;
                    }
                }
            }
            learn = [.. learn.OrderBy(x => x.DisplayOrder)];
            return (learn, dp);
        }
    }
}
