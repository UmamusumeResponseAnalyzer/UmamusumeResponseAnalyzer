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

            int turn = @event.data.chara_info.turn;
            int totalTurn = @event.IsScenario(ScenarioType.LArc) ? 67 : 78;
            //向未来借一些pt
            var borrowPtFromFuture = turn >= 60 ? 300 + 80 * (totalTurn - turn) :
                turn >= 40 ? 300 + 80 * (totalTurn - 60) + 40 * (60 - turn) :
                300 + 80 * (totalTurn - 60) + 40 * (60 - 40);

            var originSP = @event.data.chara_info.skill_point;
            var totalSP = @event.data.chara_info.skill_point + borrowPtFromFuture;

            var dpResult = Handler.Handlers.DP(tips, ref totalSP, @event.data.chara_info);
            var dpScore = dpResult.Item2;//多少pt能买多少分的技能

            double maxValue = Double.MinValue;
            int maxIndex = -1;

            for (int i = originSP; i <= originSP + borrowPtFromFuture; i++)
            {
                double currentValue = dpScore[i] - ptRate * i;
                if (currentValue > maxValue)
                {
                    maxValue = currentValue;
                    maxIndex = i;
                }
            }

            var willLearnPoint = dpScore[maxIndex];
            var remainPt = originSP - maxIndex;
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
                    previousLearnPoint += skills[i.skill_id] == null ? 0 : skills[i.skill_id].Grade;
                    
                }
            }

            //计算边际性价比与减少50/100/150/.../500pt的平均性价比
            //AnsiConsole.MarkupLine($"{previousLearnPoint} {willLearnPoint} {remainPt}");
            return willLearnPoint + previousLearnPoint + ptRate * remainPt;
        }
    }
}
