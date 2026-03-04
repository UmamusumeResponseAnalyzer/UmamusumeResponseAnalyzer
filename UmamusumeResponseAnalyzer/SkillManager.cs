using System.Runtime.CompilerServices;
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
                1 => 10,
                2 => 20,
                3 => 30,
                4 => 35,
                5 => 40,
                _ => 0
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
            if (skill.Propers.Length == 0) return;

            skill.Grade = skill.Propers.Max(i =>
            {
                var grade = skill.Grade;
                // 泥地技能似乎不受适性影响，gamewith报告为1.0，bwiki报告为+120，按gw的试试
                //switch (i.Ground)
                //{
                //    case SkillProper.GroundType.Dirt:
                //        grade = ApplyProperLevel(grade, chara_info.proper_ground_dirt);
                //        break;
                //    case SkillProper.GroundType.Turf:
                //        grade = ApplyProperLevel(grade, chara_info.proper_ground_turf);
                //        break;
                //}
                grade = i.Style switch
                {
                    SkillProper.StyleType.Nige => ApplyProperLevel(grade, chara_info.proper_running_style_nige),
                    SkillProper.StyleType.Senko => ApplyProperLevel(grade, chara_info.proper_running_style_senko),
                    SkillProper.StyleType.Sashi => ApplyProperLevel(grade, chara_info.proper_running_style_sashi),
                    SkillProper.StyleType.Oikomi => ApplyProperLevel(grade, chara_info.proper_running_style_oikomi),
                    _ => grade,
                };
                grade = i.Distance switch
                {
                    SkillProper.DistanceType.Short => ApplyProperLevel(grade, chara_info.proper_distance_short),
                    SkillProper.DistanceType.Mile => ApplyProperLevel(grade, chara_info.proper_distance_mile),
                    SkillProper.DistanceType.Middle => ApplyProperLevel(grade, chara_info.proper_distance_middle),
                    SkillProper.DistanceType.Long => ApplyProperLevel(grade, chara_info.proper_distance_long),
                    _ => grade,
                };
                return grade;
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ApplyProperLevel(int grade, int level) => level switch
        {
            8 or 7 => (int)Math.Round(grade * 1.1), //S,A
            6 or 5 => (int)Math.Round(grade * 0.9), //B,C
            4 or 3 or 2 => (int)Math.Round(grade * 0.8), //D,E,F
            1 => (int)Math.Round(grade * 0.7), //G
            _ => 0,
        };

        public SkillManager Apply(Gallop.SingleModeChara chara_info)
        {
            var tips = chara_info.skill_tips_array.SelectMany(x => Default[(x.group_id, x.rarity)])
                .Select(x => x.Clone())
                .Where(x => x.Rate > 0)
                .ToList();
            var tipIds = tips.Select(x => x.Id).ToHashSet();
            var learnedIds = chara_info.skill_array.Select(x => x.skill_id).ToHashSet();
            //添加天赋技能
            if (Database.TalentSkill.TryGetValue(chara_info.card_id, out var talents))
            {
                foreach (var talent in talents.Where(x => x.Rank <= chara_info.talent_level))
                {
                    if (!tipIds.Contains(talent.SkillId) && !learnedIds.Contains(talent.SkillId))
                    {
                        var cloned = Default[talent.SkillId].Clone();
                        tips.Add(cloned);
                        tipIds.Add(cloned.Id);
                    }
                }
            }
            foreach (var learned in chara_info.skill_array)
            {
                var cloned = Default[learned.skill_id].Clone();
                tips.Add(cloned);
                tipIds.Add(cloned.Id);
            }

            //添加上位技能缺少的下位技能（为方便计算切者技能点）
            var tipsByGroup = tips.ToLookup(x => x.GroupId);
            foreach (var group in tipsByGroup)
            {
                var maxRarity = group.Max(y => y.Rarity);
                var additionalSkills = Default.GetAllByGroupId(group.Key)
                    .Where(x => x.Rarity <= maxRarity && x.Rate > 0 && !tipIds.Contains(x.Id));
                foreach (var s in additionalSkills)
                {
                    var cloned = s.Clone();
                    tips.Add(cloned);
                    tipIds.Add(cloned.Id);
                }
            }

            var skillIndex = tips.ToDictionary(x => (x.GroupId, x.Rarity, x.Rate));

            foreach (var skill in tips)
            {
                // 同稀有度的上位技能(双圈白)
                if (skillIndex.TryGetValue((skill.GroupId, skill.Rarity, skill.Rate + 1), out var normalSuperior))
                    skill.Superior = normalSuperior;
                // 高一级稀有度的上位技能(金)
                else if (skillIndex.TryGetValue((skill.GroupId, skill.Rarity + 1, skill.Rate + 1), out var rareSuperior))
                    skill.Superior = rareSuperior;
                // 同稀有度的下位技能(单圈白)
                if (skillIndex.TryGetValue((skill.GroupId, skill.Rarity, skill.Rate - 1), out var normalInferior))
                    skill.Inferior = normalInferior;
                // 低一级稀有度的下位技能(白/双圈白)
                else if (skillIndex.TryGetValue((skill.GroupId, skill.Rarity - 1, skill.Rate - 1), out var lowerInferior))
                    skill.Inferior = lowerInferior;
            }

            var hintLevelMap = chara_info.skill_tips_array
                .ToDictionary(x => (x.group_id, x.rarity), x => x.level);

            foreach (var i in tips)
            {
                // 计算折扣
                hintLevelMap.TryGetValue((i.GroupId, i.Rarity), out var hintLevel);
                ApplyHint(i, chara_info, hintLevel);
                // 计算分数
                ApplyProper(i, chara_info);
            }

            foreach (var skill in tips.OrderByDescending(x => x.Rate))
            {
                var inferior = skill.Inferior;
                while (inferior != null)
                {
                    // 学了扣掉分数不然会加两次
                    if (learnedIds.Contains(inferior.Id))
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
        private Dictionary<int, SkillData>? _idIndex;
        private Dictionary<int, SkillData> IdIndex => _idIndex ??= list.ToDictionary(x => x.Id);

        /// <summary>
        /// 根据GroupId和Rarity获得所有同类技能(通常是单圈双圈绿)
        /// </summary>
        /// <param name="tuple">技能的GroupId、Rarity</param>
        /// <returns>所有具有相同GroupId、Rarity的技能</returns>
        public SkillData[] this[(int GroupId, int Rarity) tuple]
        {
            get => [.. list.Where(x => x.GroupId == tuple.GroupId && x.Rarity == tuple.Rarity)];
        }
        public SkillData this[int Id]
        {
            get => IdIndex.GetValueOrDefault(Id)!;
            set
            {
                if (IdIndex.TryGetValue(Id, out _))
                {
                    IdIndex[Id] = value;
                }
                else if (value != default)
                {
                    list.Add(value);
                    IdIndex[Id] = value;
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
            var willLearn = willLearnSkills ?? [];

            if (Database.TalentSkill.TryGetValue(chara_info.card_id, out var talents))
            {
                foreach (var talent in talents.Where(x => x.Rank <= chara_info.talent_level))
                {
                    if (talent.CanUpgrade(chara_info, out _, willLearn))
                    {
                        var baseSkill = this[talent.SkillId];
                        foreach (var upgradedSkillId in talent.UpgradeSkills.Keys)
                        {
                            var upgraded = SkillManagerGenerator.Default[upgradedSkillId].Clone();
                            SkillManagerGenerator.ApplyProper(upgraded, chara_info);
                            upgraded.Cost = baseSkill.Cost;
                            upgraded.IsScenarioEvolution = false;
                            baseSkill.Upgrades.Add(upgraded);
                        }
                    }
                }
            }
            //添加剧本进化
            foreach (var upgraded in Database.SkillUpgradeSpeciality.Values)
            {
                if (chara_info.scenario_id != upgraded.ScenarioId) continue;
                var baseSkill = IdIndex.GetValueOrDefault(upgraded.BaseSkillId);
                if (baseSkill == default) continue;

                foreach (var j in upgraded.UpgradeSkills)
                {
                    if (j.Value.GroupBy(x => x.Group).All(x => x.Any(y => y.IsArchived(chara_info, willLearn))))
                    {
                        var upgradedSkill = SkillManagerGenerator.Default[j.Key].Clone();
                        SkillManagerGenerator.ApplyProper(upgradedSkill, chara_info);
                        upgradedSkill.Cost = baseSkill.Cost;
                        upgradedSkill.IsScenarioEvolution = true;
                        baseSkill.Upgrades.Add(upgradedSkill);
                    }
                }
            }
        }
        public void RemoveLearned(Gallop.SingleModeChara chara_info)
        {
            var learnedIds = chara_info.skill_array.Select(x => x.skill_id).ToHashSet();
            list.RemoveAll(x => learnedIds.Contains(x.Id));
            _idIndex = null;
        }
        public IEnumerator<SkillData> GetEnumerator() => list.GetEnumerator();
        public List<SkillData> GetSkills() => list;
    }
}
