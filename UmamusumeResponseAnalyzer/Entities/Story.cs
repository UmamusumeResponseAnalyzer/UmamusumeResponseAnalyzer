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
}
