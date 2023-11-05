using Spectre.Console;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Localization;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseSkillTipsResponse(Gallop.SingleModeCheckEventResponse @event)
        {
            var tips = CalculateSkillScoreCost(@event, true);
            var totalSP = @event.data.chara_info.skill_point;

            //把天赋技能替换成进化技能
            if (Database.TalentSkill.ContainsKey(@event.data.chara_info.card_id))
            {
                var archivedConditions = @event.data.chara_info.skill_upgrade_info_array.Where(x => x.total_count == x.current_count).Select(x => x.condition_id);
                foreach (var i in Database.TalentSkill[@event.data.chara_info.card_id].Where(x => x.Rank <= @event.data.chara_info.talent_level))
                {
                    if (i.Rank != 3 && i.Rank != 5) continue;
                    var notUpgradedIndex = tips.FindIndex(x => x.Id == i.SkillId);
                    var upgradableSkills = i.UpgradeSkills.Where(x => x.Value.Intersect(archivedConditions).Any());
                    if (upgradableSkills.Any())
                    {
                        var notUpgradedSkill = tips[notUpgradedIndex];
                        var upgradedSkill = Database.Skills[upgradableSkills.First().Key];
                        upgradedSkill = upgradedSkill.Apply(@event.data.chara_info, @event.data.chara_info.skill_tips_array.FirstOrDefault(x => x.group_id == notUpgradedSkill.GroupId && x.rarity == notUpgradedSkill.Rarity)?.level ?? 0);
                        Console.WriteLine($"{upgradedSkill.Name} {upgradedSkill.Grade}");
                        upgradedSkill.Name = $"{notUpgradedSkill.Name}(进化)";
                        upgradedSkill.Cost = notUpgradedSkill.GetRealCost(@event.data.chara_info);
                        tips[notUpgradedIndex] = upgradedSkill;
                    }
                }
            }
            var dpResult = DP(tips, ref totalSP, @event.data.chara_info);

            var learn = dpResult.Item1;
            var table = new Table();
            table.Title(string.Format(Resource.MaximiumGradeSkillRecommendation_Title, @event.data.chara_info.skill_point, @event.data.chara_info.skill_point - totalSP, totalSP));
            table.AddColumns(Resource.MaximiumGradeSkillRecommendation_Columns_SkillName, Resource.MaximiumGradeSkillRecommendation_Columns_RequireSP, Resource.MaximiumGradeSkillRecommendation_Columns_Grade);
            table.Columns[0].Centered();
            foreach (var i in learn)
            {
                table.AddRow($"{i.Name}", $"{i.GetRealCost(@event.data.chara_info)}", $"{i.GetRealGrade(@event.data.chara_info)}");
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
            var willLearnPoint = learn.Sum(x => x.GetRealGrade(@event.data.chara_info));
            var totalPoint = willLearnPoint + previousLearnPoint + statusPoint;
            var thisLevelId = Database.GradeToRank.First(x => x.Min <= totalPoint && totalPoint <= x.Max).Id;
            table.Caption(string.Format($"{Resource.MaximiumGradeSkillRecommendation_Caption}", previousLearnPoint, willLearnPoint, statusPoint, totalPoint, Database.GradeToRank.First(x => x.Id == thisLevelId).Rank));
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"距离{Database.GradeToRank.First(x => x.Id == thisLevelId + 1).Rank}还有[yellow]{Database.GradeToRank.First(x => x.Id == thisLevelId + 1).Min - totalPoint}[/]分");
            AnsiConsole.MarkupLine("");

            if (@event.IsScenario(ScenarioType.GrandMasters))
            {
                GameStats.print();
                AnsiConsole.MarkupLine("");
            }

            //双适性技能的评分计算有问题，需要重做数据库
            AnsiConsole.MarkupLine("[yellow]已知问题 [/]");
            AnsiConsole.MarkupLine("[yellow]1.对于多距离或者多跑法的技能（例如英里/中距离），如果马娘对两种距离或跑法的适性不一样，评分计算可能错误 [/]");
            AnsiConsole.MarkupLine("[yellow]2.暂时没考虑进化技能，若以上没有包括可进化技能，请自己决定是否购买 [/]");
            AnsiConsole.MarkupLine("[yellow]3.没考虑紫色（负面）技能，请自己解除紫色技能 [/]");
            AnsiConsole.MarkupLine("[red]以上几种情况可以自己决定是否购买相应技能，购买之后重启游戏，即可重新计算 [/]");
            AnsiConsole.MarkupLine("[red]以下是一些参考指标 [/]");

            #region 计算边际性价比与减少50/100/150/.../500pt的平均性价比
            //计算平均性价比
            var dp = dpResult.Item2;
            var totalSP0 = totalSP;
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

            (List<SkillData>, int[]) DP(List<SkillData> tips, ref int totalSP, Gallop.SingleModeChara chara_info)
            {
                var learn = new List<SkillData>();
                // 01背包变种
                var dp = new int[totalSP + 101]; //多计算100pt，用于计算“边际性价比”
                var dpLog = Enumerable.Range(0, totalSP + 101).Select(x => new List<int>()).ToList(); // 记录dp时所选的技能，存技能Id
                for (int i = 0; i < tips.Count; i++)
                {
                    var s = tips[i];
                    // 读取此技能可以点的所有情况
                    int[] SuperiorId = { 0, 0, 0 };
                    int[] SuperiorCost = { int.MaxValue, int.MaxValue, int.MaxValue };
                    int[] SuperiorGrade = { int.MinValue, int.MinValue, int.MinValue };

                    SuperiorId[0] = s.Id;
                    SuperiorCost[0] = s.GetRealCost(chara_info);
                    SuperiorGrade[0] = s.GetRealGrade(chara_info);

                    //if (SuperiorCost[0] == 234)//skill_id=202531, debug
                    //    AnsiConsole.WriteLine($"{s.Name} {s.Id} {s.Grade} {s.Inferior.Apply(chara_info).Name} {s.Inferior.Apply(chara_info).Id} {s.Inferior.Apply(chara_info).Grade}");

                    if (SuperiorCost[0] != 0 && s.Inferior != null)
                    {
                        s = s.Inferior.Apply(chara_info);
                        SuperiorId[1] = s.Id;
                        SuperiorCost[1] = s.GetRealCost(chara_info);
                        SuperiorGrade[1] = s.GetRealGrade(chara_info);
                        if (SuperiorCost[1] != 0 && s.Inferior != null)
                        {
                            s = s.Inferior.Apply(chara_info);
                            SuperiorId[2] = s.Id;
                            SuperiorCost[2] = s.GetRealCost(chara_info);
                            SuperiorGrade[2] = s.GetRealGrade(chara_info);
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
                // 读取最终选择的技能
                var learnSkillId = dpLog[totalSP];
                foreach (var id in learnSkillId)
                {
                    foreach (var skill in tips)
                    {
                        var inferior = skill.Inferior?.Apply(chara_info);
                        var inferiorest = inferior?.Inferior?.Apply(chara_info);
                        if (skill.Id == id)
                        {
                            learn.Add(skill);
                            totalSP -= skill.GetRealCost(chara_info);
                            continue;
                        }
                        else if (inferior != null && inferior.Id == id)
                        {
                            learn.Add(inferior);
                            totalSP -= inferior.GetRealCost(chara_info);
                            continue;
                        }
                        else if (inferiorest != null && inferiorest.Id == id)
                        {
                            learn.Add(inferiorest);
                            totalSP -= inferiorest.GetRealCost(chara_info);
                        }
                    }
                }
                learn = learn.OrderBy(x => x.DisplayOrder).ToList();
                return (learn, dp);
            }
        }
    }
}
