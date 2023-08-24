using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Localization;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer.AI
{
    public class AiUtils
    {
        public static double calculateSkillScore(Gallop.SingleModeCheckEventResponse @event, double ptRate)
        {
            bool hasUnknownSkills = false;
            var totalSP = @event.data.chara_info.skill_point;
            //
            var tipsRaw = @event.data.chara_info.skill_tips_array;
            var tipsExistInDatabase = tipsRaw.Where(x => Database.Skills[(x.group_id, x.rarity)] != null);//去掉数据库中没有的技能，避免报错
            var tipsNotExistInDatabase = tipsRaw.Where(x => Database.Skills[(x.group_id, x.rarity)] == null);//数据库中没有的技能
            foreach (var i in tipsNotExistInDatabase)
            {
                hasUnknownSkills = true;
                string lineToPrint = $"警告：未知技能，group_id={i.group_id}, rarity={i.rarity}";
                for (int rarity = 0; rarity < 10; rarity++)
                {
                    var maybeInferiorSkills = Database.Skills[(i.group_id, rarity)];
                    if (maybeInferiorSkills != null)
                    {
                        foreach (var inferiorSkill in maybeInferiorSkills)
                        {
                            lineToPrint += $"，可能是 {inferiorSkill.Name} 的上位技能";
                        }
                    }
                }
                AnsiConsole.MarkupLine($"[red]{lineToPrint}[/]");
            }
            //翻译技能tips方便使用
            var tips = tipsExistInDatabase
                .Select(x => Database.Skills[(x.group_id, x.rarity)].Select(y => y.Apply(@event.data.chara_info, x.level)))
                .SelectMany(x => x)
                .Where(x => x.Rate > 0 && !@event.data.chara_info.skill_array.Any(y => y.skill_id == x.Id))
                .ToList();
            //添加天赋技能
            bool unknownUma = false;//新出的马娘的天赋技能不在数据库中
            if (Database.TalentSkill.ContainsKey(@event.data.chara_info.card_id))
            {
                foreach (var i in Database.TalentSkill[@event.data.chara_info.card_id].Where(x => x.Rank <= @event.data.chara_info.talent_level))
                {
                    if (!tips.Any(x => x.Id == i.SkillId) && !@event.data.chara_info.skill_array.Any(y => y.skill_id == i.SkillId))
                    {
                        tips.Add(Database.Skills[i.SkillId].Apply(@event.data.chara_info));
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

            //把已买技能和它们的下位去掉
            var boughtSkillsAndInferiors = new List<int>();
            foreach (var i in @event.data.chara_info.skill_array)
            {
                var skill = Database.Skills[i.skill_id];
                if (i.skill_id >= 1000000) continue; // Carnival bonus
                if (skill == null)
                {
                    hasUnknownSkills = true;
                    AnsiConsole.MarkupLine($"[red]警告：未知已购买技能，id={i.skill_id}[/]");
                    continue;
                }
                boughtSkillsAndInferiors.Add(skill.Id);
                if (skill.Inferior != null)
                {
                    skill = skill.Inferior;
                    boughtSkillsAndInferiors.Add(skill.Id);
                    if (skill.Inferior != null)
                    {
                        skill = skill.Inferior;
                        boughtSkillsAndInferiors.Add(skill.Id);
                    }

                }
            }
            tips.RemoveAll(x => boughtSkillsAndInferiors.Contains(x.Id));


            // 01背包变种
            var dp = new int[totalSP + 101]; //多计算100pt，用于计算“边际性价比”
            var dpLog = Enumerable.Range(0, totalSP + 101).Select(x => new List<int>()).ToList(); // 记录dp时所选的技能，存技能Id

            //扣除已买技能的开销与分数
            int getCost(SkillData s)
            {
                if (@event.data.chara_info.skill_array.Any(x => x.skill_id == s.Id))
                    return 0;
                else if (s.Inferior == null)
                {
                    return s.Cost;
                }
                else
                {
                    SkillData inferior = s.Inferior.Apply(@event.data.chara_info);
                    return s.Cost + getCost(inferior);
                }
            }

            //双适性技能的评分计算有问题，需要重做数据库
            int getGrade(SkillData s)
            {
                if (@event.data.chara_info.skill_array.Any(x => x.skill_id == s.Id))
                    return 0;
                else if (s.Inferior == null)
                {
                    return s.Grade;
                }
                else
                {
                    SkillData inferior = s.Inferior.Apply(@event.data.chara_info);
                    return s.Grade - inferior.Grade + getGrade(inferior);
                }
            }

            for (int i = 0; i < tips.Count; i++)
            {
                var s = tips[i];
                // 读取此技能可以点的所有情况
                int[] SuperiorId = { 0, 0, 0 };
                int[] SuperiorCost = { int.MaxValue, int.MaxValue, int.MaxValue };
                int[] SuperiorGrade = { int.MinValue, int.MinValue, int.MinValue };

                SuperiorId[0] = s.Id;
                SuperiorCost[0] = getCost(s);
                SuperiorGrade[0] = getGrade(s);

                //if (SuperiorCost[0] == 234)//skill_id=202531, debug
                //    AnsiConsole.WriteLine($"{s.Name} {s.Id} {s.Grade} {s.Inferior.Apply(@event.data.chara_info).Name} {s.Inferior.Apply(@event.data.chara_info).Id} {s.Inferior.Apply(@event.data.chara_info).Grade}");

                if (SuperiorCost[0] != 0 && s.Inferior != null)
                {
                    s = s.Inferior.Apply(@event.data.chara_info);
                    SuperiorId[1] = s.Id;
                    SuperiorCost[1] = getCost(s);
                    SuperiorGrade[1] = getGrade(s);
                    if (SuperiorCost[1] != 0 && s.Inferior != null)
                    {
                        s = s.Inferior.Apply(@event.data.chara_info);
                        SuperiorId[2] = s.Id;
                        SuperiorCost[2] = getCost(s);
                        SuperiorGrade[2] = getGrade(s);
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
                    int[] choice =
                    {
                        dp[j],
                        j  - SuperiorCost[0] >= 0 ?
                            dp[j - SuperiorCost[0]] + SuperiorGrade[0] :
                            -1,

                        j  - SuperiorCost[1] >= 0 ?
                            dp[j - SuperiorCost[1]] + SuperiorGrade[1] :
                            -1,

                        j  - SuperiorCost[2] >= 0 ?
                            dp[j - SuperiorCost[2]] + SuperiorGrade[2] :
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
            var totalSP0 = totalSP;
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
                        totalSP -= getCost(skill);
                        continue;
                    }
                    else if (inferior != null && inferior.Id == id)
                    {
                        learn.Add(inferior);
                        totalSP -= getCost(inferior);
                        continue;
                    }
                    else if (inferiorest != null && inferiorest.Id == id)
                    {
                        learn.Add(inferiorest);
                        totalSP -= getCost(inferiorest);
                    }
                }
            }
            learn = learn.OrderBy(x => x.DisplayOrder).ToList();

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
                    if (Database.Skills[i.skill_id] == null) continue;
                    var (GroupId, Rarity, Rate) = Database.Skills.Deconstruction(i.skill_id);
                    previousLearnPoint += Database.Skills[i.skill_id] == null ? 0 : Database.Skills[i.skill_id].Apply(@event.data.chara_info, 0).Grade;
                }
            }
            var willLearnPoint = learn.Sum(x => getGrade(x));
            if (unknownUma)
            {
                AnsiConsole.MarkupLine($"[red]未知马娘：{@event.data.chara_info.card_id}，无法获取觉醒技能，请自己决定是否购买。[/]");
            }
            if (hasUnknownSkills)
            {
                AnsiConsole.MarkupLine($"[red]警告：存在未知技能[/]");
            }





            //计算边际性价比与减少50/100/150/.../500pt的平均性价比


            return willLearnPoint + previousLearnPoint + ptRate * totalSP;




        }
    }
}
