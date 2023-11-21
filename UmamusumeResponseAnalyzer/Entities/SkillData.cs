using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class SkillData
    {
        public SkillData? Superior { get; set; }
        public SkillData? Inferior { get; set; }
        public string Name { get; set; }
        public int Id { get; init; }
        public int GroupId { get; init; }
        public int Rarity { get; init; }
        public int Rate { get; init; }
        public int Grade { get; set; }
        public int Cost { get; set; }
        public int DisplayOrder { get; init; }
        public SkillProper[] Propers { get; init; }
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
        public static IEnumerable<(int ScenarioId, int Rank, int ConditionId)> SCENARIO_CONDITIONS = new int[] { 6030101, 6030201, 6050101, 6050201 }.Select(x =>
        {
            var str = x.ToString();
            return ((int)char.GetNumericValue(str[0]), (int)char.GetNumericValue(str[2]), x);
        });
        public int SkillId;
        public int Rank;
        public Dictionary<int, UpgradeDetail[]> UpgradeSkills = [];
        public bool CanUpgrade(Gallop.SingleModeChara chara_info, out int upgradedSkillId, IEnumerable<SkillData>? willLearnSkills)
        {
            var currentScenarioConditions = SCENARIO_CONDITIONS.Where(x => x.ScenarioId == chara_info.scenario_id && x.Rank == Rank);
            var upgradeInfo = chara_info.skill_upgrade_info_array.Where(x => currentScenarioConditions.Any(y => y.ConditionId == x.condition_id));
            if (upgradeInfo.All(x => x.current_count == x.total_count))
            {
                upgradedSkillId = UpgradeSkills.Keys.First();
                return true;
            }
            foreach (var i in UpgradeSkills)
            {
                if (i.Value.Any(x => x.CanUpgrade(chara_info, willLearnSkills)))
                {
                    upgradedSkillId = i.Key;
                    return true;
                }
            }
            upgradedSkillId = default;
            return false;
        }

        public class UpgradeDetail
        {
            /// <summary>
            /// Condition id
            /// </summary>
            public int UpgradedSkillId;
            public UpgradeCondition[] Conditions;
            public bool CanUpgrade(Gallop.SingleModeChara chara_info, IEnumerable<SkillData>? willLearnSkills)
            {
                if (chara_info.skill_upgrade_info_array == null) return false;
                var serverCondition = chara_info.skill_upgrade_info_array.FirstOrDefault(x => Conditions.Any(y => y.ConditionId == x.condition_id));
                if (serverCondition?.current_count == serverCondition?.total_count) return true;
                var skills = chara_info.skill_array.Select(x => SkillManagerGenerator.Default[x.skill_id]);
                if (willLearnSkills != null)
                    skills = [.. skills, .. willLearnSkills];
                foreach (var condition in Conditions)
                {
                    switch (condition.Type)
                    {
                        case UpgradeCondition.ConditionType.Proper:
                            {
                                if (condition.Requirement >= 1 && condition.Requirement <= 4)
                                {
                                    var properType = condition.Requirement switch
                                    {
                                        1 => SkillProper.StyleType.Nige,
                                        2 => SkillProper.StyleType.Senko,
                                        3 => SkillProper.StyleType.Sashi,
                                        4 => SkillProper.StyleType.Oikomi,
                                    };
                                    return skills.Count(x => x.Propers.Any(y => y.Style == properType)) >= condition.AdditionalRequirement;
                                }
                                if (condition.Requirement >= 5 && condition.Requirement <= 8)
                                {
                                    var properType = condition.Requirement switch
                                    {
                                        5 => SkillProper.DistanceType.Short,
                                        6 => SkillProper.DistanceType.Mile,
                                        7 => SkillProper.DistanceType.Middle,
                                        8 => SkillProper.DistanceType.Long,
                                    };
                                    return skills.Count(x => x.Propers.Any(y => y.Distance == properType)) >= condition.AdditionalRequirement;
                                }
                                if (condition.Requirement == 9)
                                {
                                    var properType = SkillProper.GroundType.Dirt;
                                    return skills.Count(x => x.Propers.Any(y => y.Ground == properType)) >= condition.AdditionalRequirement;
                                }
                                throw new Exception($"出现了预料外的Requirement");
                            }
                        case UpgradeCondition.ConditionType.Specific:
                            return skills.Any(x => x.Id == condition.Requirement);
                        case UpgradeCondition.ConditionType.Speed:
                            return skills.Count(x => x.Category == SkillCategory.Speed) >= condition.Requirement;
                        case UpgradeCondition.ConditionType.Acceleration:
                            return skills.Count(x => x.Category == SkillCategory.Acceleration) >= condition.Requirement;
                        case UpgradeCondition.ConditionType.Recovery:
                            return skills.Count(x => x.Category == SkillCategory.Recovery) >= condition.Requirement;
                        case UpgradeCondition.ConditionType.Lane:
                            return skills.Count(x => x.Category == SkillCategory.Lane) >= condition.Requirement;
                        case UpgradeCondition.ConditionType.Stat:
                            return skills.Count(x => x.Category == SkillCategory.Stat) >= condition.Requirement;
                    }
                }
                return false;
            }

            public class UpgradeCondition
            {
                public int ConditionId;
                public ConditionType Type;
                public int Requirement;
                public int AdditionalRequirement;

                public enum ConditionType
                {
                    None,
                    Proper,
                    Specific,
                    Speed,
                    Acceleration,
                    Recovery,
                    Lane,
                    Stat
                }
            }
        }
    }
}
