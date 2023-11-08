using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Localization;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        //按技能性价比排序
        public static List<SkillData> CalculateSkillScoreCost(Gallop.SingleModeCheckEventResponse @event, bool removeInferiors)
        {
            AnsiConsole.MarkupLine($"[green]-----------------------------------------------------------------[/]");
            bool hasUnknownSkills = false;
            var totalSP = @event.data.chara_info.skill_point;
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
                if (i.Any())
                {
                    foreach (var j in i.OrderByDescending(x => x.Rarity).ThenByDescending(x => x.Rate))
                    {
                        j.TotalCost = j.Cost + i.Where(x => x.Id != j.Id).Sum(x => x.Cost);
                    }
                }
            }

            if (removeInferiors)
            {
                // 保证技能列表中的列表都是最上位技能（有下位技能则去除）
                // 理想中tips里应只保留最上位技能，其所有的下位技能都去除
                var inferiors = tips
                        .SelectMany(x => Database.Skills.GetAllByGroupId(x.GroupId))
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
                    do
                    {
                        skill = skill.Inferior;
                        boughtSkillsAndInferiors.Add(skill.Id);
                    } while (skill.Inferior != null);
                }
            }
            tips.RemoveAll(x => boughtSkillsAndInferiors.Contains(x.Id));

            if (unknownUma)
            {
                AnsiConsole.MarkupLine($"[red]未知马娘：{@event.data.chara_info.card_id}，无法获取觉醒技能，请自己决定是否购买。[/]");
            }
            if (hasUnknownSkills)
            {
                AnsiConsole.MarkupLine($"[red]警告：存在未知技能[/]");
            }
            return tips;
        }
    }
}
