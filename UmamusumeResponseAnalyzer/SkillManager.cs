using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer
{
    public class SkillManagerGenerator()
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
        internal static void ApplyProper(SkillData skill, Gallop.SingleModeChara chara_info)
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
            var tips = chara_info.skill_tips_array.SelectMany(x => Default[(x.group_id, x.rarity)])
                .Select(x => x.Clone())
                .Where(x => x.Rate > 0)
                .ToList();
            //添加天赋技能
            if (Database.TalentSkill.TryGetValue(chara_info.card_id, out var talents))
            {
                foreach (var talent in talents.Where(x => x.Rank <= chara_info.talent_level))
                {
                    if (!tips.Any(x => x.Id == talent.SkillId) && !chara_info.skill_array.Any(y => y.skill_id == talent.SkillId))
                    {
                        tips.Add(Default[talent.SkillId].Clone());
                    }
                }
            }
            foreach (var learned in chara_info.skill_array)
            {
                tips.Add(Default[learned.skill_id].Clone());
            }
            //添加上位技能缺少的下位技能（为方便计算切者技能点）
            foreach (var group in tips.GroupBy(x => x.GroupId))
            {
                var additionalSkills = Default.GetAllByGroupId(group.Key)
                    .Where(x => x.Rarity <= group.Max(y => y.Rarity))
                    .Where(x => x.Rate > 0);
                var ids = additionalSkills.ExceptBy(tips.Select(x => x.Id), x => x.Id);
                tips.AddRange(ids.Select(x => x.Clone()));
            }
            foreach (var skill in tips)
            {
                // 同稀有度的上位技能(双圈白)
                var normalSuperior = tips.FirstOrDefault(x => x.GroupId == skill.GroupId && x.Rarity == skill.Rarity && x.Rate == skill.Rate + 1);
                // 高一级稀有度的上位技能(金)
                var rareSuperior = tips.FirstOrDefault(x => x.GroupId == skill.GroupId && x.Rarity == skill.Rarity + 1 && x.Rate == skill.Rate + 1);
                if (normalSuperior != null)
                    skill.Superior = normalSuperior;
                else if (rareSuperior != null)
                    skill.Superior = rareSuperior;

                // 同稀有度的下位技能(单圈白)
                var normalInferior = tips.FirstOrDefault(x => x.GroupId == skill.GroupId && x.Rarity == skill.Rarity && x.Rate == skill.Rate - 1);
                // 低一级稀有度的下位技能(白/双圈白)
                var lowerInferior = tips.FirstOrDefault(x => x.GroupId == skill.GroupId && x.Rarity == skill.Rarity - 1 && x.Rate == skill.Rate - 1);
                if (normalInferior != null)
                    skill.Inferior = normalInferior;
                else if (lowerInferior != null)
                    skill.Inferior = lowerInferior;
            }
            foreach (var i in tips)
            {
                // 计算折扣
                ApplyHint(i, chara_info, chara_info.skill_tips_array.FirstOrDefault(x => x.group_id == i.GroupId && x.rarity == i.Rarity)?.level ?? 0);
                // 计算分数
                ApplyProper(i, chara_info);
            }
            foreach (var skill in tips.OrderByDescending(x => x.Rate))
            {
                var inferior = skill.Inferior;
                while (inferior != null)
                {
                    // 学了扣掉分数不然会加两次
                    if (chara_info.skill_array.Any(x => x.skill_id == inferior.Id))
                    {
                        skill.Grade -= inferior.Grade;
                        break;
                    }
                    // 否则把价格加上去
                    else
                    {
                        skill.Cost += inferior.Cost;
                    }
                    inferior = inferior.Inferior;
                }
            }
            return new SkillManager(tips);
        }
    }
    public class SkillManager(List<SkillData> list)
    {
        /// <summary>
        /// 根据GroupId和Rarity获得所有同类技能(通常是单圈双圈绿)
        /// </summary>
        /// <param name="tuple">技能的GroupId、Rarity</param>
        /// <returns>所有具有相同GroupId、Rarity的技能</returns>
        public SkillData[] this[(int GroupId, int Rarity) tuple]
        {
            get
            {
                return [.. list.Where(x => x.GroupId == tuple.GroupId && x.Rarity == tuple.Rarity)];
            }
        }
        public SkillData this[int Id]
        {
            get
            {
                return list.FirstOrDefault(x => x.Id == Id)!;
            }
            set
            {
                var skill = list.FirstOrDefault(x => x.Id == Id);
                if (skill == default)
                {
                    if (value != default)
                        list.Add(value);
                }
                else
                {
                    skill = value;
                }
            }
        }
        public (int GroupId, int Rarity, int Rate) Deconstruction(int Id) => this[Id].Deconstruction();
        /// <summary>
        /// 获得某个技能的所有子技能(金、双圈、单圈、×)
        /// </summary>
        /// <param name="groupId">技能的GroupId</param>
        /// <returns>所有具有相同GroupId的技能</returns>
        public SkillData[] GetAllByGroupId(int groupId) => [.. list.Where(x => x.GroupId == groupId)];
        public SkillData? GetSkillByName(string name) => list.FirstOrDefault(x => x.Name == name);

        public void Evolve(Gallop.SingleModeChara chara_info, IEnumerable<SkillData> willLearnSkills = null!)
        {
            list.ForEach(x => x.Upgrades.Clear());
            if (Database.TalentSkill.TryGetValue(chara_info.card_id, out var talents))
            {
                foreach (var talent in talents.Where(x => x.Rank <= chara_info.talent_level))
                {
                    if (talent.CanUpgrade(chara_info, out _, willLearnSkills ?? []))
                    {
                        foreach (var upgradedSkillId in talent.UpgradeSkills.Keys)
                        {
                            var upgraded = SkillManagerGenerator.Default[upgradedSkillId].Clone();
                            SkillManagerGenerator.ApplyProper(upgraded, chara_info);
                            upgraded.Cost = this[talent.SkillId].Cost;
                            upgraded.IsScenarioEvolution = false;
                            this[talent.SkillId].Upgrades.Add(upgraded);
                        }
                    }
                }
            }
            //添加剧本进化
            foreach (var upgraded in Database.SkillUpgradeSpeciality.Values)
            {
                var baseSkill = list.FirstOrDefault(x => x.Id == upgraded.BaseSkillId);
                if (baseSkill != default && chara_info.scenario_id == upgraded.ScenarioId)
                {
                    foreach (var j in upgraded.UpgradeSkills)
                    {
                        if (j.Value.GroupBy(x => x.Group).All(x => x.Any(y => y.IsArchived(chara_info, willLearnSkills ?? []))))
                        {
                            var upgradedSkill = SkillManagerGenerator.Default[j.Key].Clone();
                            SkillManagerGenerator.ApplyProper(upgradedSkill, chara_info);
                            upgradedSkill.Cost = list.First(x => x.Id == upgraded.BaseSkillId).Cost;
                            upgradedSkill.IsScenarioEvolution = true;
                            baseSkill.Upgrades.Add(upgradedSkill);
                        }
                    }
                }
            }
        }
        public void RemoveLearned(Gallop.SingleModeChara chara_info)
        {
            list.RemoveAll(x => chara_info.skill_array.Any(y => y.skill_id == x.Id));
        }
        public IEnumerator<SkillData> GetEnumerator() => list.GetEnumerator();
        public List<SkillData> GetSkills() => list;
    }
}
