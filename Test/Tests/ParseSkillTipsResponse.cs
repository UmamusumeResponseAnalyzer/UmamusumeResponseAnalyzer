using Gallop;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Handler;

namespace Test.Tests
{
    public class ParseSkillTipsResponseTest
    {
        public SingleModeCheckEventResponse @event;
        public List<UmamusumeResponseAnalyzer.Entities.SkillData> skillData;
        public ParseSkillTipsResponseTest(SingleModeCheckEventResponse singleModeCheckEventResponse)
        {
            @event = singleModeCheckEventResponse;
            skillData = null!;
        }

        public ParseSkillTipsResponseTest RemoveSkill(int id)
        {
            @event.data.chara_info.skill_tips_array =
                @event.data.chara_info.skill_tips_array.Where(x => !Database.Skills[(x.group_id, x.rarity)].Any(y => y.Id == id)).ToArray();
            return this;
        }
        public ParseSkillTipsResponseTest AddSkill(int id, int level = 0)
        {
            var t = Database.Skills[id];
            @event.data.chara_info.skill_tips_array =
                @event.data.chara_info.skill_tips_array.Append(new SkillTips
                {
                    group_id = t.GroupId,
                    rarity = t.Rarity,
                    level = level
                }).ToArray();
            return this;
        }
        public ParseSkillTipsResponseTest Translate()
        {
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
            //添加下位技能
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
            skillData = tips;
            return this;
        }
        public ParseSkillTipsResponseTest PrintSkillTips()
        {
            var table = new Table();
            table.AddColumns("技能名", "技能点", "评价点");
            foreach (var i in skillData)
            {
                table.AddRow(i.Name, i.TotalCost.ToString(), i.Grade.ToString());
            }
            AnsiConsole.Write(table);
            return this;
        }
        public ParseSkillTipsResponseTest Run()
        {
            Handlers.ParseSkillTipsResponse(@event);
            return this;
        }
    }
    public static class ParseSkillTipsResponseTestExtension
    {
        public static ParseSkillTipsResponseTest AsParseSkillTipsResponseTest(this SingleModeCheckEventResponse obj)
        {
            return new ParseSkillTipsResponseTest(obj);
        }
    }
}
