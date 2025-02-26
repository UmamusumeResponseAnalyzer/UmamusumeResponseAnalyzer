using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer
{
    public class SkillManagerGenerator(IEnumerable<SkillData> list)
    {
        public static SkillManager Default;
        /// <summary>
        /// 根据马的属性应用折扣，改变技能的价格
        /// </summary>
        /// <param name="chara_info">@event.data.chara_info</param>
        /// <param name="level">该技能的折扣等级</param>
        /// <returns></returns>
        private static void ApplyHint(SkillData skill, Gallop.SingleModeChara chara_info, int level)
        {
            var cutted = chara_info.chara_effect_id_array.Contains(7) ? 10 : 0; //切者
            var off = level switch //打折等级
            {
                0 => 0,
                1 => 10,
                2 => 20,
                3 => 30,
                4 => 35,
                5 => 40
            };
            skill.Cost = skill.Cost * (100 - off - cutted) / 100;
            // 猜测游戏内hint level排序顺序为先看最高的白，再看金，一致的看DisplayOrder
            if (skill.Rarity == 2)
            {
                var infLvl = skill.Inferior?.HintLevel ?? 0;
                skill.HintLevel = infLvl > level ? infLvl : level;
            }
            else
            {
                skill.HintLevel = level;
            }
        }
        /// <summary>
        /// 根据马的属性应用相性加成，改变技能的分数
        /// </summary>
        /// <param name="chara_info">@event.data.chara_info</param>
        private static void ApplyProper(SkillData skill, Gallop.SingleModeChara chara_info)
        {
            // 仅在技能有触发条件时应用，假设通用技能分数固定不变
            if (skill.Propers.Length != 0)
            {
                skill.Grade = skill.Propers.Max(i =>
                {
                    var grade = skill.Grade;
                    // 泥地技能似乎不受适性影响，gamewith报告为1.0，bwiki报告为+120，按gw的试试
                    //switch (i.Ground)
                    //{
                    //    case SkillProper.GroundType.Dirt:
                    //        grade = applyProperLevel(grade, chara_info.proper_ground_dirt);
                    //        break;
                    //    case SkillProper.GroundType.Turf:
                    //        grade = applyProperLevel(grade, chara_info.proper_ground_turf);
                    //        break;
                    //}
                    switch (i.Style)
                    {
                        case SkillProper.StyleType.Nige:
                            grade = applyProperLevel(grade, chara_info.proper_running_style_nige);
                            break;
                        case SkillProper.StyleType.Senko:
                            grade = applyProperLevel(grade, chara_info.proper_running_style_senko);
                            break;
                        case SkillProper.StyleType.Sashi:
                            grade = applyProperLevel(grade, chara_info.proper_running_style_sashi);
                            break;
                        case SkillProper.StyleType.Oikomi:
                            grade = applyProperLevel(grade, chara_info.proper_running_style_oikomi);
                            break;
                    }
                    switch (i.Distance)
                    {
                        case SkillProper.DistanceType.Short:
                            grade = applyProperLevel(grade, chara_info.proper_distance_short);
                            break;
                        case SkillProper.DistanceType.Mile:
                            grade = applyProperLevel(grade, chara_info.proper_distance_mile);
                            break;
                        case SkillProper.DistanceType.Middle:
                            grade = applyProperLevel(grade, chara_info.proper_distance_middle);
                            break;
                        case SkillProper.DistanceType.Long:
                            grade = applyProperLevel(grade, chara_info.proper_distance_long);
                            break;
                    }
                    return grade;
                });

                static int applyProperLevel(int grade, int level) => level switch
                {
                    8 or 7 => (int)Math.Round(grade * 1.1), //S,A
                    6 or 5 => (int)Math.Round(grade * 0.9), //B,C
                    4 or 3 or 2 => (int)Math.Round(grade * 0.8), //D,E,F
                    1 => (int)Math.Round(grade * 0.7), //G
                    _ => 0,
                };
            }
        }
        public SkillManager Apply(Gallop.SingleModeChara chara_info)
        {
            var skills = new List<SkillData>(list.Select(x => x.Clone()));
            foreach (var skill in skills)
            {
                // 同组技能
                var group = skills.Where(x => x.GroupId == skill.GroupId);
                if (group.Any())
                {
                    // 同稀有度的上位技能(双圈白)
                    var normalSuperior = group.FirstOrDefault(x => x.Rarity == skill.Rarity && x.Rate == skill.Rate + 1);
                    // 高一级稀有度的上位技能(金)
                    var rareSuperior = group.FirstOrDefault(x => x.Rarity == skill.Rarity + 1 && x.Rate == skill.Rate + 1);
                    if (normalSuperior != null && chara_info.skill_tips_array.Any(x => x.group_id == normalSuperior.GroupId && x.rarity == normalSuperior.Rarity))
                        skill.Superior = normalSuperior;
                    else if (rareSuperior != null && chara_info.skill_tips_array.Any(x => x.group_id == rareSuperior.GroupId && x.rarity == rareSuperior.Rarity))
                        skill.Superior = rareSuperior;

                    // 同稀有度的下位技能(单圈白)
                    var normalInferior = group.FirstOrDefault(x => x.Rarity == skill.Rarity && x.Rate == skill.Rate - 1);
                    // 低一级稀有度的下位技能(白/双圈白)
                    var lowerInferior = group.FirstOrDefault(x => x.Rarity == skill.Rarity - 1 && x.Rate == skill.Rate - 1);
                    if (normalInferior != null)
                        skill.Inferior = normalInferior;
                    else if (lowerInferior != null)
                        skill.Inferior = lowerInferior;
                }
            }
            foreach (var skill in skills.OrderBy(x => x.Rarity).OrderBy(x => x.Rate))
            {
                // 计算折扣
                ApplyHint(skill, chara_info, chara_info.skill_tips_array.FirstOrDefault(x => x.group_id == skill.GroupId && x.rarity == skill.Rarity)?.level ?? 0);
                // 计算分数
                ApplyProper(skill, chara_info);
            }
            foreach (var skill in skills.OrderByDescending(x => x.Rate))
            {
                var inferior = skill.Inferior;
                while (inferior != null)
                {
                    // 学了
                    if (chara_info.skill_array.Any(x => x.skill_id == inferior.Id))
                    {
                        skill.Grade -= inferior.Grade;
                        break;
                    }
                    else
                    {
                        skill.Cost += inferior.Cost;
                    }
                    inferior = inferior.Inferior;
                }
            }
            return new SkillManager(skills);
        }
    }
    public class SkillManager(IEnumerable<SkillData> list)
    {
        readonly Dictionary<int, SkillData> idMap = list.ToDictionary(x => x.Id, x => x);
        readonly Dictionary<(int GroupId, int Rarity, int Rate), SkillData> rateMap = list.ToDictionary(x => (x.GroupId, x.Rarity, x.Rate), x => x);
        readonly Dictionary<(int GroupId, int Rarity), SkillData[]> rarityMap = list.GroupBy(x => (x.GroupId, x.Rarity)).ToDictionary(x => x.Key, x => x.ToArray());

        public SkillData this[(int GroupId, int Rarity, int Rate) tuple]
        {
            get => rateMap.TryGetValue(tuple, out var value) ? value : null!;
            set => rateMap[tuple] = value;
        }
        /// <summary>
        /// 根据GroupId和Rarity获得所有同类技能(通常是单圈双圈绿)
        /// </summary>
        /// <param name="tuple">技能的GroupId、Rarity</param>
        /// <returns>所有具有相同GroupId、Rarity的技能</returns>
        public SkillData[] this[(int GroupId, int Rarity) tuple]
        {
            get => rarityMap.TryGetValue(tuple, out var value) ? value : null!;
            set => rarityMap[tuple] = value;
        }
        public SkillData this[int Id]
        {
            get => idMap.TryGetValue(Id, out var value) ? value : null!;
            set => idMap[Id] = value;
        }
        public bool TryGetValue(int id, out SkillData? value) => idMap.TryGetValue(id, out value);
        public (int GroupId, int Rarity, int Rate) Deconstruction(int Id) => this[Id].Deconstruction();
        /// <summary>
        /// 获得某个技能的所有子技能(金、双圈、单圈、×)
        /// </summary>
        /// <param name="groupId">技能的GroupId</param>
        /// <returns>所有具有相同GroupId的技能</returns>
        public SkillData[] GetAllByGroupId(int groupId) => idMap.Where(x => x.Value.GroupId == groupId).Select(x => x.Value).ToArray();
    }
}
