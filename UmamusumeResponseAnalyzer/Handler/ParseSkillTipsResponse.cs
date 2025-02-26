using Spectre.Console;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;
using static UmamusumeResponseAnalyzer.Localization.Handlers.ParseSkillTipsResponse;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseSkillTipsResponse(Gallop.SingleModeCheckEventResponse @event)
        {
            var skills = Database.Skills.Apply(@event.data.chara_info);
            var tips = CalculateSkillScoreCost(@event, skills, true);
            var totalSP = @event.data.chara_info.skill_point;
            // 可以进化的天赋技能，即觉醒3、5的那两个金技能
            var upgradableTalentSkills = Database.TalentSkill[@event.data.chara_info.card_id].Where(x => x.Rank <= @event.data.chara_info.talent_level && (x.Rank == 3 || x.Rank == 5));

            var dpResult = DP(tips, ref totalSP, @event.data.chara_info);
            var learn = ReplaceAllSkillWithUpgradeSkill(@event, skills, upgradableTalentSkills, dpResult.Item1).ToList();
            var willLearnPoint = learn.Sum(x => x.Grade);

            var table = new Table();
            table.Title(string.Format(I18N_Title, @event.data.chara_info.skill_point, @event.data.chara_info.skill_point - totalSP, totalSP));
            table.AddColumns(I18N_Columns_SkillName, I18N_Columns_RequireSP, I18N_Columns_Grade);
            table.Columns[0].Centered();
            foreach (var i in learn)
            {
                table.AddRow($"{i.Name}", $"{i.Cost}", $"{i.Grade}");
            }
            var statusPoint = Database.StatusToPoint[@event.data.chara_info.speed]
                            + Database.StatusToPoint[@event.data.chara_info.stamina]
                            + Database.StatusToPoint[@event.data.chara_info.power]
                            + Database.StatusToPoint[@event.data.chara_info.guts]
                            + Database.StatusToPoint[@event.data.chara_info.wiz];
            AnsiConsole.MarkupLine($"[yellow]速{@event.data.chara_info.speed} 耐{@event.data.chara_info.stamina} 力{@event.data.chara_info.power} 根{@event.data.chara_info.guts} 智{@event.data.chara_info.wiz} [/]");
            var previousLearnPoint = 0; //之前学的技能的累计评价点
            foreach (var i in @event.data.chara_info.skill_array)
            {
                if (i.skill_id > 1000000 && i.skill_id < 2000000) continue; // 嘉年华&LoH技能
                if (i.skill_id.ToString()[0] == '1' && i.skill_id > 100000 && i.skill_id < 200000) //3*固有
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
                    if (upgradableSkills != default && upgradableSkills.CanUpgrade(@event.data.chara_info, out var upgradedSkillId, dpResult.Item1))
                    {
                        previousLearnPoint += skills[upgradedSkillId] == null ? 0 : skills[upgradedSkillId].Grade;
                    }
                    else
                    {
                        previousLearnPoint += skills[i.skill_id] == null ? 0 : skills[i.skill_id].Grade;
                    }
                }
            }
            var totalPoint = willLearnPoint + previousLearnPoint + statusPoint;
            var thisLevelId = Database.GradeToRank.First(x => x.Min <= totalPoint && totalPoint <= x.Max).Id;
            table.Caption(string.Format(I18N_Caption, previousLearnPoint, willLearnPoint, statusPoint, totalPoint, Database.GradeToRank.First(x => x.Id == thisLevelId).Rank));
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine(I18N_ScoreToNextGrade, Database.GradeToRank.First(x => x.Id == thisLevelId + 1).Rank, Database.GradeToRank.First(x => x.Id == thisLevelId + 1).Min - totalPoint);
            AnsiConsole.MarkupLine(string.Empty);

            if (@event.IsScenario(ScenarioType.GrandMasters))
            {
                GameStats.Print();
                AnsiConsole.MarkupLine(string.Empty);
            }

            AnsiConsole.MarkupLine(I18N_ScoreCalculateAttention_1);
            AnsiConsole.MarkupLine(I18N_ScoreCalculateAttention_2);
            AnsiConsole.MarkupLine(I18N_ScoreCalculateAttention_3);
            AnsiConsole.MarkupLine(I18N_ScoreCalculateAttention_4);
            AnsiConsole.MarkupLine(I18N_ScoreCalculateAttention_5);

            #region 计算边际性价比与减少50/100/150/.../500pt的平均性价比
            //计算平均性价比
            var dp = dpResult.Item2;
            var totalSP0 = @event.data.chara_info.skill_point;
            if (totalSP0 > 0)
                AnsiConsole.MarkupLine(I18N_AverageCostEffectiveness, ((double)willLearnPoint / totalSP0).ToString("F3"));
            //计算边际性价比，对totalSP正负50的范围做线性回归
            if (totalSP0 > 50)
            {
                double sxy = 0, sy = 0, sx2 = 0, n = 0;
                for (var x = -50; x <= 50; x++)
                {
                    var y = dp[totalSP0 + x];
                    sxy += x * y;
                    sy += y;
                    sx2 += x * x;
                    n += 1;
                }
                var b = sxy / sx2;
                AnsiConsole.MarkupLine(I18N_MarginalCostEffectiveness, b.ToString("F3"));
            }
            //计算减少50/100/150/.../500pt的平均性价比
            AnsiConsole.MarkupLine(I18N_ExpectedCostEffectiveness);
            for (var t = 1; t <= 10; t++)
            {
                var start = totalSP0 - t * 50 - 25;
                if (start < 0)
                    break;

                //totalSP - t * 50 的前后25个取平均
                var meanScoreReduced = dp.Skip(start).Take(51).Average();
                var eff = (dp[totalSP0] - meanScoreReduced) / (t * 50);
                AnsiConsole.MarkupLine(I18N_ExpectedCostEffectivenessByPrice, t * 50, eff.ToString("F3"));
            }
            #endregion
        }
        public static IEnumerable<SkillData> ReplaceAllSkillWithUpgradeSkill(Gallop.SingleModeCheckEventResponse @event, SkillManager skillmanager, IEnumerable<TalentSkillData> upgradableTalentSkills, List<SkillData> willLearnSkills)
        {
            #region 角色进化
            if (upgradableTalentSkills != null)
            {
                foreach (var upgradableSkill in upgradableTalentSkills)
                {
                    var notUpgradedIndex = willLearnSkills.FindIndex(x => x.Id == upgradableSkill.SkillId); // 天赋技能在tips中的位置
                    if (notUpgradedIndex == -1) continue; // 如果没找到则说明已学会，不再需要
                    var notUpgradedSkill = willLearnSkills[notUpgradedIndex]; // 原本的天赋技能

                    // 判定不学习时是否能进化，有多个可进化技能时按第一个计算
                    if (upgradableSkill.CanUpgrade(@event.data.chara_info, out var upgradedSkillId, willLearnSkills))
                    {
                        var upgradedSkill = skillmanager[upgradedSkillId].Clone();
                        upgradedSkill.Name = $"{notUpgradedSkill.Name}(角色{I18N_Evolved})";
                        upgradedSkill.Cost = notUpgradedSkill.Cost;

                        var inferior = notUpgradedSkill.Inferior;
                        while (inferior != null)
                        {
                            // 学了下位技能，则减去下位技能的分数
                            if (@event.data.chara_info.skill_array.Any(x => x.skill_id == inferior.Id))
                            {
                                upgradedSkill.Grade -= inferior.Grade;
                                break;
                            }
                            inferior = inferior.Inferior;
                        }

                        willLearnSkills[notUpgradedIndex] = upgradedSkill;
                    }
                }
            }
            #endregion
            #region 剧本进化
#warning TODO
            /// 目前是只考虑进化前两个可进化技能。有没有可能进化后的技能分数也有高低？
            var evolvedCount = 0; // 剧本进化最多两个，但是可进化的可能更多
            foreach (var i in Database.SkillUpgradeSpeciality.Keys)
            {
                if (evolvedCount >= 2) continue;
                var baseSkillId = i.Item1;
                var requireScenario = i.Item2;
                var spec = Database.SkillUpgradeSpeciality[i];
                // 如果不是对应剧本或没有可进化的基础技能的Hint
                if (@event.data.chara_info.scenario_id != requireScenario || !skillmanager.TryGetValue(baseSkillId, out var _)) continue;
                foreach (var j in spec.UpgradeSkills)
                {
                    var upgradedSkillId = j.Key;
                    if (j.Value.GroupBy(x => x.Group).All(x => x.Any(y => y.IsArchived(@event.data.chara_info, willLearnSkills))))
                    {
                        var notUpgradedIndex = willLearnSkills.FindIndex(x => x.Id == baseSkillId); // 天赋技能在tips中的位置
                        if (notUpgradedIndex == -1) continue; // 如果没找到则说明已学会，不再需要
                        var notUpgradedSkill = willLearnSkills[notUpgradedIndex]; // 原本的天赋技能

                        var upgradedSkill = skillmanager[upgradedSkillId].Clone();
                        upgradedSkill.Name = $"{notUpgradedSkill.Name}(剧本{I18N_Evolved})";
                        upgradedSkill.Cost = notUpgradedSkill.Cost;

                        var inferior = notUpgradedSkill.Inferior;
                        while (inferior != null)
                        {
                            // 学了下位技能，则减去下位技能的分数
                            if (@event.data.chara_info.skill_array.Any(x => x.skill_id == inferior.Id))
                            {
                                upgradedSkill.Grade -= inferior.Grade;
                                break;
                            }
                            inferior = inferior.Inferior;
                        }
                        willLearnSkills[notUpgradedIndex] = upgradedSkill;
                        evolvedCount += 1;
                    }
                    break;
                }
            }
#endregion
            return willLearnSkills;
        }
    }
}
