using System.Diagnostics.CodeAnalysis;
using UmamusumeResponseAnalyzer.Entities;
using SingleModeChara = Gallop.SingleModeChara;

namespace UmamusumeResponseAnalyzer
{
    public sealed class SkillManagerGenerator
    {
        private readonly SkillManager defaults;

        public SkillManagerGenerator(IEnumerable<SkillData> skills)
        {
            defaults = new(skills.Select(x => x.Clone()));
        }

        public bool TryFindByName(string name, [NotNullWhen(true)] out SkillData? skill)
        {
            if (defaults.TryFindByName(name, out var found))
            {
                skill = found.Clone();
                return true;
            }

            skill = null;
            return false;
        }
        /// <summary>
        /// 根据马的属性应用折扣，改变技能的价格
        /// </summary>
        /// <param name="chara_info">@event.data.chara_info</param>
        /// <param name="level">该技能的折扣等级</param>
        /// <returns></returns>
        private static void ApplyHint(SkillData skill, SingleModeChara chara_info, int level)
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
                skill.HintLevel = Math.Max(skill.Inferior?.HintLevel ?? 0, level);
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
        internal static void ApplyProper(SkillData skill, SingleModeChara chara_info)
        {
            // 仅在技能有触发条件时应用，假设通用技能分数固定不变
            if (skill.Propers.Length != 0)
            {
                skill.Grade = skill.Propers.Max(i =>
                {
                    var grade = skill.Grade;
                    switch (i.Style)
                    {
                        case SkillProper.StyleType.Nige:
                            grade = ApplyProperLevel(grade, chara_info.proper_running_style_nige);
                            break;
                        case SkillProper.StyleType.Senko:
                            grade = ApplyProperLevel(grade, chara_info.proper_running_style_senko);
                            break;
                        case SkillProper.StyleType.Sashi:
                            grade = ApplyProperLevel(grade, chara_info.proper_running_style_sashi);
                            break;
                        case SkillProper.StyleType.Oikomi:
                            grade = ApplyProperLevel(grade, chara_info.proper_running_style_oikomi);
                            break;
                    }
                    switch (i.Distance)
                    {
                        case SkillProper.DistanceType.Short:
                            grade = ApplyProperLevel(grade, chara_info.proper_distance_short);
                            break;
                        case SkillProper.DistanceType.Mile:
                            grade = ApplyProperLevel(grade, chara_info.proper_distance_mile);
                            break;
                        case SkillProper.DistanceType.Middle:
                            grade = ApplyProperLevel(grade, chara_info.proper_distance_middle);
                            break;
                        case SkillProper.DistanceType.Long:
                            grade = ApplyProperLevel(grade, chara_info.proper_distance_long);
                            break;
                    }
                    return grade;
                });

                static int ApplyProperLevel(int grade, int level) => level switch
                {
                    8 or 7 => (int)Math.Round(grade * 1.1), //S,A
                    6 or 5 => (int)Math.Round(grade * 0.9), //B,C
                    4 or 3 or 2 => (int)Math.Round(grade * 0.8), //D,E,F
                    1 => (int)Math.Round(grade * 0.7), //G
                    _ => 0,
                };
            }
        }
        public SkillManager Apply(SingleModeChara chara_info)
        {
            var tips = chara_info.skill_tips_array.SelectMany(x => defaults.FindByGroup(x.group_id, x.rarity))
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
                        tips.Add(defaults.GetRequiredById(talent.SkillId).Clone());
                    }
                }
            }
            foreach (var learned in chara_info.skill_array)
            {
                tips.Add(defaults.GetRequiredById(learned.skill_id).Clone());
            }
            //添加上位技能缺少的下位技能（为方便计算切者技能点）
            foreach (var group in tips.GroupBy(x => x.GroupId))
            {
                var additionalSkills = defaults.FindByGroup(group.Key)
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
                    skill.Cost += inferior.Cost;
                    inferior = inferior.Inferior;
                }
            }
            return new SkillManager(tips, this);
        }

        internal SkillData GetRequiredById(int id) => defaults.GetRequiredById(id);
    }

    public sealed class SkillManager : IEnumerable<SkillData>
    {
        private readonly List<SkillData> list;
        private readonly SkillManagerGenerator? source;

        public SkillManager(IEnumerable<SkillData> skills)
            : this(skills, null)
        {
        }

        internal SkillManager(IEnumerable<SkillData> skills, SkillManagerGenerator? source)
        {
            list = [.. skills];
            this.source = source;
        }
        public SkillData[] FindByGroup(int groupId, int rarity)
            => [.. list.Where(x => x.GroupId == groupId && x.Rarity == rarity)];

        public SkillData[] FindByGroup(int groupId)
            => [.. list.Where(x => x.GroupId == groupId)];

        public bool TryFindById(int id, [NotNullWhen(true)] out SkillData? skill)
        {
            skill = list.FirstOrDefault(x => x.Id == id);
            return skill is not null;
        }

        public SkillData GetRequiredById(int id)
            => list.FirstOrDefault(x => x.Id == id)
                ?? throw new KeyNotFoundException(string.Format(Localization.Database.I18N_SkillMissing, id));

        public bool TryFindByName(string name, [NotNullWhen(true)] out SkillData? skill)
        {
            skill = list.FirstOrDefault(x => x.Name == name);
            return skill is not null;
        }

        public (int GroupId, int Rarity, int Rate) Deconstruction(int id)
            => GetRequiredById(id).Deconstruction();

        public void Evolve(SingleModeChara chara_info, IEnumerable<SkillData> willLearnSkills = null!)
        {
            var defaults = source ?? throw new InvalidOperationException(Localization.Database.I18N_SkillManagerSourceMissing);
            list.ForEach(x => x.Upgrades.Clear());
            willLearnSkills ??= [];
            if (Database.TalentSkill.TryGetValue(chara_info.card_id, out var talents))
            {
                foreach (var talent in talents.Where(x => x.Rank <= chara_info.talent_level))
                {
                    if (talent.CanUpgrade(chara_info, out _, willLearnSkills))
                    {
                        foreach (var upgradedSkillId in talent.UpgradeSkills.Keys)
                        {
                            var upgraded = defaults.GetRequiredById(upgradedSkillId).Clone();
                            SkillManagerGenerator.ApplyProper(upgraded, chara_info);
                            upgraded.Cost = GetRequiredById(talent.SkillId).Cost;
                            upgraded.IsScenarioEvolution = false;
                            GetRequiredById(talent.SkillId).Upgrades.Add(upgraded);
                        }
                    }
                }
            }
            //添加剧本进化
            foreach (var upgraded in Database.SkillUpgradeSpeciality.Values)
            {
                if (TryFindById(upgraded.BaseSkillId, out var baseSkill)
                    && chara_info.scenario_id == upgraded.ScenarioId)
                {
                    foreach (var j in upgraded.UpgradeSkills)
                    {
                        if (j.Value.GroupBy(x => x.Group).All(x => x.Any(y => y.IsArchived(chara_info, willLearnSkills))))
                        {
                            var upgradedSkill = defaults.GetRequiredById(j.Key).Clone();
                            SkillManagerGenerator.ApplyProper(upgradedSkill, chara_info);
                            upgradedSkill.Cost = baseSkill.Cost;
                            upgradedSkill.IsScenarioEvolution = true;
                            baseSkill.Upgrades.Add(upgradedSkill);
                        }
                    }
                }
            }
        }
        public void RemoveLearned(SingleModeChara chara_info)
        {
            list.RemoveAll(x => chara_info.skill_array.Any(y => y.skill_id == x.Id));
        }
        public IEnumerator<SkillData> GetEnumerator() => list.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public IReadOnlyList<SkillData> GetSkills() => list;
    }
}
