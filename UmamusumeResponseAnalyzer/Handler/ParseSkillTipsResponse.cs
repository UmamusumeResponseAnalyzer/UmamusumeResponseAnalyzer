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
            
            // 保证技能列表中的列表都是最上位技能（有下位技能则去除），避免引入判断是否获取了上位技能的数组
            // 不知道dp时通过Skill.Inferior/Superior读到的技能cost是否apply了hint，姑且认为是apply了
            // 理想中tips里边应该是只保留最上位技能，下位技能去除
            for (int i = 0; i < tips.Count; i++)
            {
                var s = tips[i];
                if (s.Inferior != null)
                {
                    // 如果此技能有下位, 在列表中去除该下位技能
                    for (int j = 0; j < tips.Count; j++)
                    {
                        if (tips[j].Id == s.Inferior.Id)
                        {
                            tips.RemoveAt(j);
                            i = 0;  // 不从头重新来无法遍历tips全部技能，暂且不知为何
                        }
                    }
                }
                if (s.Superior != null && s.Name.Contains("○") && s.Superior.Name.Contains("◎"))
                {
                    // 保证有单双圈之分的技能为双圈
                    tips.RemoveAt(i);
                    tips.Add(s.Superior);
                    i = 0;
                }
            }
            AnsiConsole.WriteLine("--- finishDeDuplicate ---");   //debug
            for (int i = 0; i < tips.Count; i++)    //debug
            {
                var s = tips[i];
                var info = string.Format("{0} cost:{1} value:{2}", s.Name, s.Cost, s.Grade); //debug
                if (s.Inferior != null)
                {
                    info = info + " " + s.Inferior.Name + " " + s.Inferior.Cost;    //debug
                }
                if (s.Superior != null)
                {
                    info = info + " " + s.Superior.Name + " " + s.Superior.Cost;    //debug
                }
                AnsiConsole.WriteLine(info);    //debug
            }
            
            // 01背包变种
            var dp = new int[totalSP + 1];
            var dpLog = new List<int>[totalSP + 1]; // 记录dp时所选的技能，存技能Id

            for (int i = 0; i < totalSP + 1; i++)
            {
                dp[i] = 0;
                dpLog[i] = new List<int>();
            }
            for (int i = 0; i < tips.Count; i++)
            {
                var s = tips[i];
                // 读取此技能可以点的所有情况
                int[] SuperiorCost = { 10000, 10000 };
                int[] SuperiorGrade = { -1, -1 };
                if (s.Inferior != null)
                {
                    // 绝大多数金技能
                    SuperiorCost[0] = s.TotalCost;
                    SuperiorGrade[0] = s.Grade;
                    s = s.Inferior;
                    AnsiConsole.WriteLine(string.Format("{0} {1} {2} | {3} {4} {5} |{6} {7} ", s.Name, s.Cost, s.Grade, s.Superior.Name, SuperiorCost[0], SuperiorGrade[0], SuperiorCost[1], SuperiorGrade[1])); //debug
                    if (s.Inferior != null)
                    {
                        // 绝大多数金绿技能
                        SuperiorCost[1] = SuperiorCost[0];
                        SuperiorGrade[1] = SuperiorGrade[0];
                        SuperiorCost[0] = s.TotalCost;
                        SuperiorGrade[0] = s.Grade;
                        s = s.Inferior;
                    }
                    // 退化技能到最低级，方便选择
                }
                else
                    AnsiConsole.WriteLine(string.Format("{0} {1} {2}", s.Name, s.Cost, s.Grade)); //debug

                for (int j = totalSP; j >= s.Cost; j--)
                {
                    // 背包四种选法
                    // 1-不选
                    // 2-只选此技能
                    // 3-选这个技能和它的上一级技能
                    // 4-选这个技能的最高位技（全点）
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
                        dpStatusSucceed(j - s.Cost);
                        dpLog[j].Add(s.Id);
                    }
                    else if (IsBestOption(2))
                    {
                        dp[j] = choice[2];
                        dpStatusSucceed(j - SuperiorCost[0]);
                        dpLog[j].Add(s.Superior.Id);
                    }
                    else if (IsBestOption(3))
                    {
                        dp[j] = choice[3];
                        dpStatusSucceed(j - SuperiorCost[1]);
                        dpLog[j].Add(s.Superior.Superior.Id);
                    }

                    bool IsBestOption(int index)
                    {
                        bool IsBest = true;
                        for (int k = 0; k < 4; k++)
                            IsBest = choice[index] >= choice[k] && IsBest;
                        return IsBest;
                    };

                    void dpStatusSucceed(int index)
                    {
                        dpLog[j] = new List<int>();
                        for (int k = 0; k < dpLog[index].Count; k++)
                            dpLog[j].Add(dpLog[index][k]);
                    }
                }
            }

            // 读取最终选择的技能
            var learnSkillId = dpLog[totalSP];
            var learn = new List<SkillManager.SkillData>();
            foreach (var id in learnSkillId)
            {
                foreach (var skill in tips)
                {
                    if (skill.Id == id) { learn.Add(skill); continue; }
                    else if (skill.Inferior != null)
                    {
                        if (skill.Inferior.Id == id) { learn.Add(skill.Inferior); continue; }
                        else if (skill.Inferior.Inferior != null)
                        {
                            if (skill.Inferior.Inferior.Id == id) { learn.Add(skill.Inferior.Inferior); continue; }
                        }
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
