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
        public List<Choice> Choices { get; set; } = new();

    }
    public class Choice
    {
        public string Option { get; set; } = string.Empty;
        public string SuccessEffect { get; set; } = string.Empty;
        public string FailedEffect { get; set; } = string.Empty;
    }
    public class SuccessStory
    {
        public string Name { get; set; } = string.Empty;
        public List<SuccessChoice> Choices { get; set; } = new();
    }
    public class SuccessChoice
    {
        /// <summary>
        /// 服务器下发的choice_array的index，从0开始
        /// </summary>
        public int ChoiceIndex { get; set; }
        /// <summary>
        /// 服务器下发的SelectIndex，即第几个选项
        /// </summary>
        public int SelectIndex { get; set; }
        /// <summary>
        /// 事件效果，Key为限定的剧本ID（部分事件需要，如赛后事件），不限定剧本ID的Key统一为0
        /// </summary>
        public SuccessChoiceEffectDictionary Effects { get; set; } = new();//ScenarioId-Effect
    }
    public class SuccessChoiceEffectDictionary : Dictionary<int, string>
    {
        public new bool ContainsKey(int key) => base.ContainsKey(key) || base.ContainsKey(0);
        public new string this[int key]
        {
            get => base.ContainsKey(key) ? base[key] : base[0]; //如果有对应剧本的效果则返回，否则返回通用
            set => base[key] = value;
        }
    }
}
