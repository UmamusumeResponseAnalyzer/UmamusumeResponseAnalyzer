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
            var skills = Database.Skills.Apply(@event.data.chara_info);
            var tips = Handler.Handlers.CalculateSkillScoreCost(@event, skills, true);
            var totalSP = @event.data.chara_info.skill_point;
            // 可以进化的天赋技能，即觉醒3、5的那两个金技能
            var upgradableTalentSkills = Database.TalentSkill[@event.data.chara_info.card_id].Where(x => x.Rank <= @event.data.chara_info.talent_level && (x.Rank == 3 || x.Rank == 5));

            var dpResult = Handler.Handlers.DP(tips, ref totalSP, @event.data.chara_info);
            // 把天赋技能替换成进化技能
            if (Database.TalentSkill.ContainsKey(@event.data.chara_info.card_id))
            {
                foreach (var upgradableSkill in upgradableTalentSkills)
                {
                    var notUpgradedIndex = tips.FindIndex(x => x.Id == upgradableSkill.SkillId);
                    if (notUpgradedIndex == -1) continue;
                    var notUpgradedSkill = tips[notUpgradedIndex];

                    // 加上要学习的技能后再判定是否能进化
                    if (upgradableSkill.CanUpgrade(@event.data.chara_info, out var upgradedSkillId, dpResult.Item1))
                    {
                        var upgradedSkill = skills[upgradedSkillId];
                        upgradedSkill.Name = $"{notUpgradedSkill.Name}(进化)";
                        upgradedSkill.Cost = notUpgradedSkill.Cost;
                        // 进化技能分数 -= 金技能总分数 - 金技能分数(即-=基础已学习技能分数)
                        upgradedSkill.Grade -= notUpgradedSkill.Grade;
                        tips[notUpgradedIndex] = upgradedSkill;
                    }
                }
            }
            var learn = dpResult.Item1;
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
                    if (skills[i.skill_id] == null) continue;
                    var (GroupId, Rarity, Rate) = skills.Deconstruction(i.skill_id);
                    var upgradableSkills = upgradableTalentSkills.FirstOrDefault(x => x.SkillId == i.skill_id);
                    // 学了可进化的技能，且满足进化条件，则按进化计算分数
                    if (upgradableSkills != default && upgradableSkills.CanUpgrade(@event.data.chara_info, out var upgradedSkillId, learn))
                    {
                        previousLearnPoint += skills[upgradedSkillId] == null ? 0 : skills[upgradedSkillId].Grade;
                    }
                    else
                    {
                        previousLearnPoint += skills[i.skill_id] == null ? 0 : skills[i.skill_id].Grade;
                    }
                }
            }
            var willLearnPoint = learn.Sum(x => x.Grade);
            //计算边际性价比与减少50/100/150/.../500pt的平均性价比
            return willLearnPoint + previousLearnPoint + ptRate * totalSP;
        }
    }
}
