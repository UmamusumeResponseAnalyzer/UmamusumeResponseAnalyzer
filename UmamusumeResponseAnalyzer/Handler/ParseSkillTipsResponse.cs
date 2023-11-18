using Spectre.Console;
using System.Linq;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Localization;

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
                        tips[notUpgradedIndex] = upgradedSkill;
                    }
                }
            }
            var learn = dpResult.Item1;
            var table = new Table();
            table.Title(string.Format(Resource.MaximiumGradeSkillRecommendation_Title, @event.data.chara_info.skill_point, @event.data.chara_info.skill_point - totalSP, totalSP));
            table.AddColumns(Resource.MaximiumGradeSkillRecommendation_Columns_SkillName, Resource.MaximiumGradeSkillRecommendation_Columns_RequireSP, Resource.MaximiumGradeSkillRecommendation_Columns_Grade);
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
            var willLearnPoint = learn.Sum(x => x.Grade);
            var totalPoint = willLearnPoint + previousLearnPoint + statusPoint;
            var thisLevelId = Database.GradeToRank.First(x => x.Min <= totalPoint && totalPoint <= x.Max).Id;
            table.Caption(string.Format($"{Resource.MaximiumGradeSkillRecommendation_Caption}", previousLearnPoint, willLearnPoint, statusPoint, totalPoint, Database.GradeToRank.First(x => x.Id == thisLevelId).Rank));
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"距离{Database.GradeToRank.First(x => x.Id == thisLevelId + 1).Rank}还有[yellow]{Database.GradeToRank.First(x => x.Id == thisLevelId + 1).Min - totalPoint}[/]分");
            AnsiConsole.MarkupLine(string.Empty);

            if (@event.IsScenario(ScenarioType.GrandMasters))
            {
                GameStats.Print();
                AnsiConsole.MarkupLine(string.Empty);
            }

            //双适性技能的评分计算有问题，需要重做数据库
            AnsiConsole.MarkupLine("[yellow]已知问题 [/]");
            AnsiConsole.MarkupLine("[yellow]1.对于学习技能后才能判定是否能进化的技能，暂时以无法进化考虑。若以上没有包括可进化技能，请自己决定是否购买 [/]");
            AnsiConsole.MarkupLine("[yellow]2.没考虑紫色（负面）技能，请自己解除紫色技能 [/]");
            AnsiConsole.MarkupLine("[red]以上几种情况可以自己决定是否购买相应技能，购买之后重启游戏，即可重新计算 [/]");
            AnsiConsole.MarkupLine("[red]以下是一些参考指标 [/]");

            #region 计算边际性价比与减少50/100/150/.../500pt的平均性价比
            //计算平均性价比
            var dp = dpResult.Item2;
            var totalSP0 = @event.data.chara_info.skill_point;
            if (totalSP0 > 0)
                AnsiConsole.MarkupLine($"[aqua]平均性价比：{(double)willLearnPoint / totalSP0:F3}[/]");
            //计算边际性价比，对totalSP正负50的范围做线性回归
            if (totalSP0 > 50)
            {
                double sxy = 0, sy = 0, sx2 = 0, n = 0;
                for (int x = -50; x <= 50; x++)
                {
                    int y = dp[totalSP0 + x];
                    sxy += x * y;
                    sy += y;
                    sx2 += x * x;
                    n += 1;
                }
                double b = sxy / sx2;
                AnsiConsole.MarkupLine($"[aqua]边际性价比：{b:F3}[/]");
            }
            //计算减少50/100/150/.../500pt的平均性价比
            AnsiConsole.MarkupLine($"[aqua]不同价格的技能的期望性价比如下，若某技能的评分计算错误且偏低且没有出现在上表中（以上几种情况），请手动计算性价比与下表进行比较[/]");
            for (int t = 1; t <= 10; t++)
            {
                int start = totalSP0 - t * 50 - 25;
                if (start < 0)
                    break;

                //totalSP - t * 50 的前后25个取平均
                var meanScoreReduced = dp.Skip(start).Take(51).Average();
                var eff = (dp[totalSP0] - meanScoreReduced) / (t * 50);
                AnsiConsole.MarkupLine($"[green]{t * 50}pt技能的期望性价比：{eff:F3}[/]");
            }
            #endregion
        }
    }
}
