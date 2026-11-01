using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SingleModeChara = Gallop.SingleModeChara;
using static UmamusumeResponseAnalyzer.Entities.TalentSkillData;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class SkillData
    {
        private string? translatedName;
        public bool? IsScenarioEvolution { get; set; }
        public List<SkillData> Upgrades { get; set; } = [];
        /// <summary>
        /// 上位技能(不包含进化技能)
        /// </summary>
        public SkillData? Superior { get; set; }
        /// <summary>
        /// 下位技能(不包含紫技能)
        /// </summary>
        public SkillData? Inferior { get; set; }
        /// <summary>
        /// 技能名
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 显示的技能名，比如翻译过的
        /// </summary>
        public string DisplayName
        {
            get => translatedName ?? Name;
            set => translatedName = value;
        }
        /// <summary>
        /// 技能ID
        /// </summary>
        public int Id { get; init; }
        /// <summary>
        /// 技能组ID，如金右、双圈右、单圈右、右×的技能组ID均为20001
        /// </summary>
        public int GroupId { get; init; }
        /// <summary>
        /// 稀有度，通常1为白、2为金
        /// </summary>
        public int Rarity { get; init; }
        /// <summary>
        /// 用于标识同稀有度的不同技能，通常金为2(双圈金为3)、白为1(双圈为2，单圈为1)、紫为-1
        /// </summary>
        public int Rate { get; init; }
        /// <summary>
        /// 技能分数
        /// </summary>
        public int Grade { get; set; }
        /// <summary>
        /// 技能价格
        /// </summary>
        public int Cost { get; set; }
        /// <summary>
        /// 打折等级，仅适用于对应游戏内排序，请不要用来进行其他计算！！
        /// </summary>
        public int HintLevel { get; set; }
        /// <summary>
        /// 技能在学习界面的显示顺序，正向排序
        /// </summary>
        public int DisplayOrder { get; init; }
        /// <summary>
        /// 技能的适性
        /// </summary>
        public SkillProper[] Propers { get; init; }
        /// <summary>
        /// 技能的种类(速度、加速度、恢复等)，仅用于计算技能进化条件
        /// </summary>
        public SkillCategory Category { get; init; }

        public (int GroupId, int Rarity, int Rate) Deconstruction() => (GroupId, Rarity, Rate);
        public SkillData Clone() => Clone(new(ReferenceEqualityComparer.Instance));

        private SkillData Clone(Dictionary<SkillData, SkillData> clones)
        {
            if (clones.TryGetValue(this, out var existing))
                return existing;

            var clone = new SkillData
            {
                IsScenarioEvolution = IsScenarioEvolution,
                Name = Name,
                translatedName = translatedName,
                Id = Id,
                GroupId = GroupId,
                Rarity = Rarity,
                Rate = Rate,
                Grade = Grade,
                Cost = Cost,
                HintLevel = HintLevel,
                DisplayOrder = DisplayOrder,
                Propers = [.. Propers.Select(x => new SkillProper
                {
                    Ground = x.Ground,
                    Distance = x.Distance,
                    Style = x.Style
                })],
                Category = Category
            };
            clones.Add(this, clone);
            clone.Upgrades = [.. Upgrades.Select(x => x.Clone(clones))];
            clone.Superior = Superior?.Clone(clones);
            clone.Inferior = Inferior?.Clone(clones);
            return clone;
        }
    }
    public enum SkillCategory
    {
        /// <summary>
        /// 绿
        /// </summary>
        Stat,
        /// <summary>
        /// 蓝
        /// </summary>
        Recovery,
        /// <summary>
        /// 速度
        /// </summary>
        Speed,
        /// <summary>
        /// 加速度
        /// </summary>
        Acceleration,
        /// <summary>
        /// 跑道
        /// </summary>
        Lane,
        /// <summary>
        /// 出闸
        /// </summary>
        Reaction,
        /// <summary>
        /// 视野
        /// </summary>
        Observation,
        /// <summary>
        /// 红
        /// </summary>
        Debuff,
        /// <summary>
        /// 特殊(大逃)
        /// </summary>
        Special
    }
    public class TalentSkillData
    {
        /// <summary>
        /// 天赋技能的ID
        /// </summary>
        public int SkillId;
        /// <summary>
        /// 天赋技能所需Rank，低于此Rank时无法进化
        /// </summary>
        public int Rank;
        /// <summary>
        /// 进化技能ID-进化具体需求(条件ID、条件详情）
        /// </summary>
        public Dictionary<int, UpgradeCondition[]> UpgradeSkills = [];
        /// <summary>
        /// 由于大部分条件达成情况服务器都会下发，这里仅计算[学习指定类型技能特定个数]、[学习特定技能](大逃等)
        /// </summary>
        public class UpgradeCondition
        {
            /// <summary>
            /// 条件ID
            /// </summary>
            public int ConditionId;
            /// <summary>
            /// 数据库中的num，同组条件取或，不同组取且
            /// </summary>
            public int Group;
            /// <summary>
            /// 条件类型
            /// </summary>
            public ConditionType Type;
            /// <summary>
            /// 条件所需内容，Type为Specific时为指定技能ID，为Proper时为指定技能适性类型，否则为所需技能数量
            /// </summary>
            public int Requirement;
            /// <summary>
            /// 条件所需额外内容，仅Type为Proper时需要，为对应适性技能的需求数量
            /// </summary>
            public int AdditionalRequirement;

            internal bool HasSupportedType => Enum.IsDefined(Type);

            public enum ConditionType
            {
                /// <summary>
                /// 仅由服务器判定，不做本地预测
                /// </summary>
                None,
                /// <summary>
                /// 需要学习指定适性(距离、场地、跑法)的技能
                /// </summary>
                Proper,
                /// <summary>
                /// 需要学习指定技能
                /// </summary>
                Specific,
                /// <summary>
                /// 需要学习速度技能
                /// </summary>
                Speed,
                /// <summary>
                /// 需要学习加速度技能
                /// </summary>
                Acceleration,
                /// <summary>
                /// 需要学习恢复技能
                /// </summary>
                Recovery,
                /// <summary>
                /// 需要学习走位技能
                /// </summary>
                Lane,
                /// <summary>
                /// 需要学习绿技能
                /// </summary>
                Stat
            }
        }
    }
    public class SkillUpgradeSpeciality
    {
        public int ScenarioId;
        public int BaseSkillId;
        public int SkillId;
        public Dictionary<int, UpgradeCondition[]> UpgradeSkills = new();
    }
}
