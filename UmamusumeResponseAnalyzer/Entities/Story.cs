using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class Story
    {
        /// <summary>
        /// 记录在master.mdb中的id
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 记录在master.mdb中的事件名
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 事件所属角色，通用事件为马娘名，决胜服/S卡事件为全名
        /// </summary>
        public string TriggerName { get; set; } = string.Empty;
        public List<List<Choice>> Choices { get; set; } = new();

    }

    public class StoryEffectValue
    {
        public List<int> Values;    // 事件属性，分别为：速耐力根智，pt，hint等级，体力，羁绊，干劲
        public List<string> SkillNames; // 获得的技能名称
        public List<string> Extras; // 其他词条（如断事件）
        public string? BuffName;    // 获得的Buff名称
    }
    public class Choice
    {
        public string Option { get; set; } = string.Empty;
        public string SuccessEffect { get; set; } = string.Empty;
        public string FailedEffect { get; set; } = string.Empty;
        public StoryEffectValue? SuccessEffectValue;
        public StoryEffectValue? FailedEffectValue;
    }
    public class SuccessStory
    {
        public int Id { get; set; }
        public SuccessChoice[][] Choices { get; set; }
    }
    public class SuccessChoice
    {
        public int SelectIndex { get; set; }
        public State State { get; set; }
        public int Scenario { get; set; }
        public string Effect { get; set; } = string.Empty;

    }
    public enum State
    {
        Unknown = -1,
        Fail = 0,
        Success = 1,
        GreatSuccess = 2,
        None = int.MaxValue
    }
    public class Extra
    {
        
    }
    public static class SuccessChoiceArrayExtension
    {
        public static IEnumerable<SuccessChoice> WithSelectIndex(this IEnumerable<SuccessChoice> arr, int selectIndex)
        {
            if (!arr.Any()) return Array.Empty<SuccessChoice>();
            return arr.Where(x => x.SelectIndex == selectIndex);
        }
        public static IEnumerable<SuccessChoice> WithScenarioId(this IEnumerable<SuccessChoice> arr, int scenarioId)
        {
            if (!arr.Any()) return Array.Empty<SuccessChoice>();
            var specified = arr.Where(x => x.Scenario == scenarioId);
            return specified.Any() ? specified : arr.Where(x => x.Scenario == 0);
        }
        public static bool TryGet(this IEnumerable<SuccessChoice> arr, out SuccessChoice choice)
        {
            choice = null!;
            if (!arr.Any()) return false;
            if (arr.Count() > 1) throw new Exception($"SuccessChoice最终应该只有1个，但是实际有{arr.Count()}个");
            choice = arr.First();
            return true;
        }
    }
}
