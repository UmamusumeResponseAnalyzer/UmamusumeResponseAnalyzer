using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class SkillData
    {
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
        public SkillData Clone()
        {
            var clone = (SkillData)MemberwiseClone();
            clone.Superior = Superior?.Clone();
            clone.Inferior = Inferior?.Clone();
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
        /// 剧本专属的进化条件，第一位固定为剧本ID，第三位固定为技能所需天赋等级(3/5)，第五位固定为技能进化需求的位置
        /// </summary>
        public static IEnumerable<(int ScenarioId, int Rank, int ConditionId)> SCENARIO_CONDITIONS = new int[] { 6030101, 6030201, 6050101, 6050201 }.Select(x =>
        {
            var str = x.ToString();
            return ((int)char.GetNumericValue(str[0]), (int)char.GetNumericValue(str[2]), x);
        });
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
        /// 判断该技能是否可进化(无论可进化的是哪个)
        /// </summary>
        /// <param name="chara_info">角色信息</param>
        /// <param name="upgradedSkillId">存在可进化的技能时为对应的技能ID(存在多个可进化的技能时为第一个)，否则为default</param>
        /// <param name="willLearnSkills">部分条件需要学习指定技能才能达成，传入的是额外纳入计算的技能</param>
        /// <returns></returns>
        public bool CanUpgrade(Gallop.SingleModeChara chara_info, out int upgradedSkillId, IEnumerable<SkillData>? willLearnSkills)
        {
            upgradedSkillId = default;
            // 不存在技能进化信息时直接返回，是针对繁中服的兼容性判断
            if (chara_info.skill_upgrade_info_array == null) return false;
            // 角色天赋等级低于该技能所需的天赋等级，无法进化
            if (chara_info.talent_level < Rank) return false;

            // 当前剧本所拥有的、针对当前天赋技能的特殊条件
            var currentScenarioConditions = SCENARIO_CONDITIONS.Where(x => x.ScenarioId == chara_info.scenario_id && x.Rank == Rank);
            // 满足的剧本特殊条件
            var scenarioUpgradeInfo = chara_info.skill_upgrade_info_array.Where(x => currentScenarioConditions.Any(y => y.ConditionId == x.condition_id));
            // 如果满足全部的剧本特殊条件则技能同样可进化，返回第一个进化技能ID
            if (scenarioUpgradeInfo.All(x => x.current_count == x.total_count))
            {
                upgradedSkillId = UpgradeSkills.Keys.First();
                return true;
            }

            var skills = chara_info.skill_array.Select(x => SkillManagerGenerator.Default[x.skill_id]);
            // 加入需要额外考虑的技能(如果有)
            if (willLearnSkills != null) skills = [.. skills, .. willLearnSkills];

            // 依次判断，返回第一个可进化的
            foreach (var i in UpgradeSkills)
            {
                // 判断如果全部进化条件均满足，则认为可进化
                if (i.Value.All(x => x.IsArchived(chara_info, skills)))
                {
                    upgradedSkillId = i.Key;
                    return true;
                }
            }
            return false;
        }
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

            public bool IsArchived(Gallop.SingleModeChara chara_info, IEnumerable<SkillData> skills)
            {
                // 由服务器保存的条件详情
                var serverCondition = chara_info.skill_upgrade_info_array.FirstOrDefault(x => x.condition_id == ConditionId);
                // 由服务器确定该条件已满足
                if (serverCondition?.current_count == serverCondition?.total_count) return true;

                switch (Type)
                {
                    case ConditionType.Proper:
                        {
                            if (Requirement >= 1 && Requirement <= 4)
                            {
                                var properType = Requirement switch
                                {
                                    1 => SkillProper.StyleType.Nige,
                                    2 => SkillProper.StyleType.Senko,
                                    3 => SkillProper.StyleType.Sashi,
                                    4 => SkillProper.StyleType.Oikomi,
                                };
                                return skills.Count(x => x.Propers.Any(y => y.Style == properType)) >= AdditionalRequirement;
                            }
                            if (Requirement >= 5 && Requirement <= 8)
                            {
                                var properType = Requirement switch
                                {
                                    5 => SkillProper.DistanceType.Short,
                                    6 => SkillProper.DistanceType.Mile,
                                    7 => SkillProper.DistanceType.Middle,
                                    8 => SkillProper.DistanceType.Long,
                                };
                                return skills.Count(x => x.Propers.Any(y => y.Distance == properType)) >= AdditionalRequirement;
                            }
                            if (Requirement == 9)
                            {
                                var properType = SkillProper.GroundType.Dirt;
                                return skills.Count(x => x.Propers.Any(y => y.Ground == properType)) >= AdditionalRequirement;
                            }
                            throw new Exception($"出现了预料外的Requirement");
                        }
                    case UpgradeCondition.ConditionType.Specific:
                        return skills.Any(x => x.Id == Requirement);
                    case UpgradeCondition.ConditionType.Speed:
                        return skills.Count(x => x.Category == SkillCategory.Speed) >= Requirement;
                    case UpgradeCondition.ConditionType.Acceleration:
                        return skills.Count(x => x.Category == SkillCategory.Acceleration) >= Requirement;
                    case UpgradeCondition.ConditionType.Recovery:
                        return skills.Count(x => x.Category == SkillCategory.Recovery) >= Requirement;
                    case UpgradeCondition.ConditionType.Lane:
                        return skills.Count(x => x.Category == SkillCategory.Lane) >= Requirement;
                    case UpgradeCondition.ConditionType.Stat:
                        return skills.Count(x => x.Category == SkillCategory.Stat) >= Requirement;
                }
                return false;
            }
            public enum ConditionType
            {
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
}
