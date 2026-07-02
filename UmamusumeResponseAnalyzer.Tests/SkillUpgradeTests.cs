using Gallop;
using UmamusumeResponseAnalyzer.Entities;
using Xunit;
using static UmamusumeResponseAnalyzer.Entities.TalentSkillData;
// Gallop 命名空间下也有同名 SkillData，被测的是 Entities 版本，显式取别名消歧义
using SkillData = UmamusumeResponseAnalyzer.Entities.SkillData;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// 技能进化判定（最大痛点）的分支级回归：<see cref="TalentSkillData.CanUpgrade"/> 与
    /// <see cref="UpgradeCondition.IsArchived"/>，外加 <see cref="SkillData"/> 的 Clone/Deconstruction/DisplayName
    /// 与 SCENARIO_CONDITIONS 的 ID 解析。
    ///
    /// 注意：被测两个方法只读取传入的 chara_info(SingleModeChara) 与 skills(IEnumerable&lt;SkillData&gt;)，
    /// 不触碰任何 Database 静态，故本测试类无需 [Collection("Database")] 与 seed——保持纯函数式输入输出。
    /// </summary>
    public class SkillUpgradeTests
    {
        // ---- 构造辅助：按被测访问的字段填齐，确保非 null ----

        /// <summary>构造一个技能；Propers 必非 null（IsArchived 的 Proper 分支会 .Any 遍历它）。</summary>
        static SkillData MakeSkill(int id, SkillCategory category = SkillCategory.Stat, SkillProper[]? propers = null) =>
            new() { Id = id, Name = $"skill{id}", Category = category, Propers = propers ?? [] };

        static SkillProper Style(SkillProper.StyleType s) => new() { Style = s };
        static SkillProper Distance(SkillProper.DistanceType d) => new() { Distance = d };
        static SkillProper Ground(SkillProper.GroundType g) => new() { Ground = g };

        /// <summary>构造 chara_info；skill_upgrade_info_array 默认给空数组（非 null），由具体用例覆盖。</summary>
        static SingleModeChara MakeChara(
            int talentLevel = 5,
            int scenarioId = 1,
            SingleModeSkillUpgrade[]? upgradeInfo = null) =>
            new()
            {
                talent_level = talentLevel,
                scenario_id = scenarioId,
                skill_upgrade_info_array = upgradeInfo ?? [],
            };

        static SingleModeSkillUpgrade Info(int conditionId, int current, int total) =>
            new() { condition_id = conditionId, current_count = current, total_count = total };

        // =========================================================================
        // SCENARIO_CONDITIONS：ID → (ScenarioId, Rank, ConditionId) 解析
        // 解析规则：ScenarioId=首位数字, Rank=第三位数字(str[2]), ConditionId=原值
        //   6030101 -> (6, 3, 6030101)   6030201 -> (6, 3, 6030201)
        //   6050101 -> (6, 5, 6050101)   6050201 -> (6, 5, 6050201)
        // =========================================================================

        [Fact]
        public void ScenarioConditions_ParsesIdsCorrectly()
        {
            var parsed = SCENARIO_CONDITIONS.ToArray();

            Assert.Equal(4, parsed.Length);
            Assert.Equal((6, 3, 6030101), parsed[0]);
            Assert.Equal((6, 3, 6030201), parsed[1]);
            Assert.Equal((6, 5, 6050101), parsed[2]);
            Assert.Equal((6, 5, 6050201), parsed[3]);
            // 全部隶属剧本6；Rank 仅取自第三位，与第二位(0)、末段无关
            Assert.All(parsed, x => Assert.Equal(6, x.ScenarioId));
        }

        // =========================================================================
        // IsArchived：服务器侧短路
        // =========================================================================

        /// <summary>condition_id 在 server 数组中存在且 current==total → 直接 true（不看 skills）。</summary>
        [Fact]
        public void IsArchived_ServerSaysComplete_ReturnsTrue()
        {
            var chara = MakeChara(upgradeInfo: [Info(100, 3, 3)]);
            // Speed 类条件，但一个 Speed 技能都没传——只因服务器标记完成而 true
            var cond = new UpgradeCondition { ConditionId = 100, Type = UpgradeCondition.ConditionType.Speed, Requirement = 99 };

            Assert.True(cond.IsArchived(chara, []));
        }

        [Fact]
        public void IsArchived_ConditionAbsentFromServer_Throws()
        {
            var chara = MakeChara(upgradeInfo: [Info(999, 0, 5)]); // 不含 ConditionId=100
            var cond = new UpgradeCondition { ConditionId = 100, Type = UpgradeCondition.ConditionType.Speed, Requirement = 99 };

            var ex = Assert.Throws<InvalidOperationException>(() => cond.IsArchived(chara, []));
            Assert.Contains("conditionId=100", ex.Message, StringComparison.Ordinal);
        }

        // =========================================================================
        // IsArchived：ConditionType.Speed/Acceleration/Recovery/Lane/Stat
        // 判定式：currentCount + skills.Count(Category == X) >= Requirement
        // currentCount = 服务器 current_count（条件存在但未完成时取它）
        // =========================================================================

        [Fact]
        public void IsArchived_Speed_CountsMatchingCategoryPlusServerProgress()
        {
            // 服务器进度 1，需求 3 → 还需 2 个 Speed 技能
            var chara = MakeChara(upgradeInfo: [Info(100, 1, 3)]);
            var cond = new UpgradeCondition { ConditionId = 100, Type = UpgradeCondition.ConditionType.Speed, Requirement = 3 };

            // 仅 1 个 Speed：1(server)+1 = 2 < 3 → false
            Assert.False(cond.IsArchived(chara, [MakeSkill(1, SkillCategory.Speed)]));
            // 2 个 Speed：1+2 = 3 >= 3 → true
            Assert.True(cond.IsArchived(chara, [MakeSkill(1, SkillCategory.Speed), MakeSkill(2, SkillCategory.Speed)]));
            // 类别不匹配（Acceleration）不计入：1+0 < 3 → false
            Assert.False(cond.IsArchived(chara, [MakeSkill(3, SkillCategory.Acceleration), MakeSkill(4, SkillCategory.Acceleration)]));
        }

        [Theory]
        [InlineData(UpgradeCondition.ConditionType.Acceleration, SkillCategory.Acceleration)]
        [InlineData(UpgradeCondition.ConditionType.Recovery, SkillCategory.Recovery)]
        [InlineData(UpgradeCondition.ConditionType.Lane, SkillCategory.Lane)]
        [InlineData(UpgradeCondition.ConditionType.Stat, SkillCategory.Stat)]
        public void IsArchived_CategoryConditions_RequireEnoughMatching(
            UpgradeCondition.ConditionType type, SkillCategory category)
        {
            // 服务器进度 0，需求 2
            var chara = MakeChara(upgradeInfo: [Info(100, 0, 2)]);
            var cond = new UpgradeCondition { ConditionId = 100, Type = type, Requirement = 2 };

            Assert.False(cond.IsArchived(chara, [MakeSkill(1, category)]));               // 0+1 < 2
            Assert.True(cond.IsArchived(chara, [MakeSkill(1, category), MakeSkill(2, category)])); // 0+2 >= 2
        }

        // =========================================================================
        // IsArchived：ConditionType.Specific
        // 判定式：skills.Any(x => x.Id == Requirement)  —— 不叠加服务器 currentCount
        // =========================================================================

        [Fact]
        public void IsArchived_Specific_RequiresExactSkillId()
        {
            var chara = MakeChara(upgradeInfo: [Info(100, 0, 1)]);
            var cond = new UpgradeCondition { ConditionId = 100, Type = UpgradeCondition.ConditionType.Specific, Requirement = 42 };

            Assert.True(cond.IsArchived(chara, [MakeSkill(42)]));                 // 含 Id=42
            Assert.True(cond.IsArchived(chara, [MakeSkill(7), MakeSkill(42)]));   // 多个里含 42
            Assert.False(cond.IsArchived(chara, [MakeSkill(7), MakeSkill(8)]));   // 不含 42
        }

        // =========================================================================
        // IsArchived：ConditionType.Proper —— 跑法(1-4)/距离(5-8)/泥地(9)
        // 判定式：currentCount + skills.Count(匹配适性) >= AdditionalRequirement
        // =========================================================================

        [Fact]
        public void IsArchived_ProperStyle_MapsRequirementToStyleType()
        {
            // Requirement=2 → Senko(先)，AdditionalRequirement=2，服务器进度 0
            var chara = MakeChara(upgradeInfo: [Info(100, 0, 2)]);
            var cond = new UpgradeCondition
            {
                ConditionId = 100,
                Type = UpgradeCondition.ConditionType.Proper,
                Requirement = 2,
                AdditionalRequirement = 2,
            };

            var senko1 = MakeSkill(1, propers: [Style(SkillProper.StyleType.Senko)]);
            var senko2 = MakeSkill(2, propers: [Style(SkillProper.StyleType.Senko)]);
            var nige = MakeSkill(3, propers: [Style(SkillProper.StyleType.Nige)]);

            Assert.False(cond.IsArchived(chara, [senko1]));          // 0+1 < 2
            Assert.True(cond.IsArchived(chara, [senko1, senko2]));   // 0+2 >= 2
            Assert.False(cond.IsArchived(chara, [senko1, nige]));    // Nige 不计：0+1 < 2
        }

        [Fact]
        public void IsArchived_ProperDistance_UsesServerProgress()
        {
            // Requirement=8 → Long(长)，AdditionalRequirement=3，服务器进度 2 → 还差 1
            var chara = MakeChara(upgradeInfo: [Info(100, 2, 3)]);
            var cond = new UpgradeCondition
            {
                ConditionId = 100,
                Type = UpgradeCondition.ConditionType.Proper,
                Requirement = 8,
                AdditionalRequirement = 3,
            };

            var longSkill = MakeSkill(1, propers: [Distance(SkillProper.DistanceType.Long)]);
            var mileSkill = MakeSkill(2, propers: [Distance(SkillProper.DistanceType.Mile)]);

            Assert.True(cond.IsArchived(chara, [longSkill]));   // 2+1 >= 3
            Assert.False(cond.IsArchived(chara, [mileSkill]));  // Mile 不计：2+0 < 3
        }

        [Fact]
        public void IsArchived_ProperDirtGround_Requirement9()
        {
            // Requirement=9 → 泥地(Dirt)，AdditionalRequirement=1，服务器进度 0
            var chara = MakeChara(upgradeInfo: [Info(100, 0, 1)]);
            var cond = new UpgradeCondition
            {
                ConditionId = 100,
                Type = UpgradeCondition.ConditionType.Proper,
                Requirement = 9,
                AdditionalRequirement = 1,
            };

            var dirt = MakeSkill(1, propers: [Ground(SkillProper.GroundType.Dirt)]);
            var turf = MakeSkill(2, propers: [Ground(SkillProper.GroundType.Turf)]);

            Assert.True(cond.IsArchived(chara, [dirt]));    // 0+1 >= 1
            Assert.False(cond.IsArchived(chara, [turf]));   // Turf 不计：0+0 < 1
        }

        // =========================================================================
        // CanUpgrade：前置短路
        // =========================================================================

        /// <summary>skill_upgrade_info_array 为 null（繁中服兼容）→ false。</summary>
        [Fact]
        public void CanUpgrade_NullUpgradeInfoArray_ReturnsFalse()
        {
            var chara = new SingleModeChara { talent_level = 5, scenario_id = 1, skill_upgrade_info_array = null! };
            var talent = new TalentSkillData
            {
                Rank = 3,
                UpgradeSkills = { [200] = [new UpgradeCondition { ConditionId = 1, Type = UpgradeCondition.ConditionType.Specific, Requirement = 1 }] },
            };

            Assert.False(talent.CanUpgrade(chara, out var upgraded, []));
            Assert.Equal(0, upgraded); // out 维持 default
        }

        /// <summary>角色天赋等级低于技能所需 Rank → false。</summary>
        [Fact]
        public void CanUpgrade_TalentLevelBelowRank_ReturnsFalse()
        {
            var chara = MakeChara(talentLevel: 2, scenarioId: 1, upgradeInfo: [Info(1, 5, 5)]);
            var talent = new TalentSkillData
            {
                Rank = 3, // 需要 3，角色只有 2
                UpgradeSkills = { [200] = [new UpgradeCondition { ConditionId = 1, Type = UpgradeCondition.ConditionType.Specific, Requirement = 42 }] },
            };

            Assert.False(talent.CanUpgrade(chara, out var upgraded, [MakeSkill(42)]));
            Assert.Equal(0, upgraded);
        }

        // =========================================================================
        // CanUpgrade：普通条件路径（非剧本特殊条件，scenario != 6）
        // 每个 entry：i.Value 按 Group 分组 → 全部组都需 Any(IsArchived)
        // =========================================================================

        /// <summary>单条件满足 → 可进化，out 为该进化技能 ID。</summary>
        [Fact]
        public void CanUpgrade_SingleConditionArchived_ReturnsTrueWithSkillId()
        {
            // scenario_id=1（非 6，不触发剧本特殊条件分支）
            // ConditionId=1 在 server 中存在且未完成(0/1)，走 Specific 判定
            var chara = MakeChara(talentLevel: 5, scenarioId: 1, upgradeInfo: [Info(1, 0, 1)]);
            var talent = new TalentSkillData
            {
                Rank = 3,
                UpgradeSkills = { [201] = [new UpgradeCondition { ConditionId = 1, Type = UpgradeCondition.ConditionType.Specific, Requirement = 42 }] },
            };

            Assert.True(talent.CanUpgrade(chara, out var upgraded, [MakeSkill(42)]));
            Assert.Equal(201, upgraded);
        }

        [Fact]
        public void CanUpgrade_SingleConditionNotArchived_ReturnsFalse()
        {
            var chara = MakeChara(talentLevel: 5, scenarioId: 1, upgradeInfo: [Info(1, 0, 1)]);
            var talent = new TalentSkillData
            {
                Rank = 3,
                UpgradeSkills = { [201] = [new UpgradeCondition { ConditionId = 1, Type = UpgradeCondition.ConditionType.Specific, Requirement = 42 }] },
            };

            // 没学到 Id=42 → Specific 不满足 → false
            Assert.False(talent.CanUpgrade(chara, out var upgraded, [MakeSkill(7)]));
            Assert.Equal(0, upgraded);
        }

        /// <summary>
        /// 同一组(Group 相同)内是「二选一」：任一满足即该组达成。
        /// 两条件同 Group=0，其一满足即整组 Any 成立 → 可进化。
        /// </summary>
        [Fact]
        public void CanUpgrade_SameGroup_IsEitherOr()
        {
            var chara = MakeChara(talentLevel: 5, scenarioId: 1, upgradeInfo: [Info(1, 0, 1), Info(2, 0, 1)]);
            var talent = new TalentSkillData
            {
                Rank = 3,
                UpgradeSkills =
                {
                    [202] =
                    [
                        new UpgradeCondition { ConditionId = 1, Group = 0, Type = UpgradeCondition.ConditionType.Specific, Requirement = 42 },
                        new UpgradeCondition { ConditionId = 2, Group = 0, Type = UpgradeCondition.ConditionType.Specific, Requirement = 43 },
                    ],
                },
            };

            // 只学到其中之一(43) → 同组 Any 成立 → true
            Assert.True(talent.CanUpgrade(chara, out var upgraded, [MakeSkill(43)]));
            Assert.Equal(202, upgraded);
        }

        /// <summary>
        /// 不同组之间是「与」：每个组都必须各自达成。
        /// Group0 满足、Group1 不满足 → All 不成立 → 不可进化。
        /// </summary>
        [Fact]
        public void CanUpgrade_DifferentGroups_AreAnded()
        {
            var chara = MakeChara(talentLevel: 5, scenarioId: 1, upgradeInfo: [Info(1, 0, 1), Info(2, 0, 1)]);
            var talent = new TalentSkillData
            {
                Rank = 3,
                UpgradeSkills =
                {
                    [203] =
                    [
                        new UpgradeCondition { ConditionId = 1, Group = 0, Type = UpgradeCondition.ConditionType.Specific, Requirement = 42 },
                        new UpgradeCondition { ConditionId = 2, Group = 1, Type = UpgradeCondition.ConditionType.Specific, Requirement = 43 },
                    ],
                },
            };

            // 仅满足 Group0(42)，Group1(43) 未满足 → false
            Assert.False(talent.CanUpgrade(chara, out _, [MakeSkill(42)]));
            // 两组都满足 → true
            Assert.True(talent.CanUpgrade(chara, out var upgraded, [MakeSkill(42), MakeSkill(43)]));
            Assert.Equal(203, upgraded);
        }

        /// <summary>多个进化技能时返回第一个达成的 key。</summary>
        [Fact]
        public void CanUpgrade_MultipleUpgrades_ReturnsFirstSatisfied()
        {
            var chara = MakeChara(talentLevel: 5, scenarioId: 1, upgradeInfo: [Info(1, 0, 1), Info(2, 0, 1)]);
            var talent = new TalentSkillData
            {
                Rank = 3,
                UpgradeSkills =
                {
                    // 第一个进化技能(210)条件未满足，第二个(211)满足
                    [210] = [new UpgradeCondition { ConditionId = 1, Type = UpgradeCondition.ConditionType.Specific, Requirement = 42 }],
                    [211] = [new UpgradeCondition { ConditionId = 2, Type = UpgradeCondition.ConditionType.Specific, Requirement = 43 }],
                },
            };

            // 只学到 43：210 不满足、211 满足 → 返回 211
            Assert.True(talent.CanUpgrade(chara, out var upgraded, [MakeSkill(43)]));
            Assert.Equal(211, upgraded);
        }

        // =========================================================================
        // CanUpgrade：剧本特殊条件分支（scenario_id==6）
        // currentScenarioConditions = SCENARIO_CONDITIONS 中 ScenarioId==6 && Rank==this.Rank
        // 满足全部对应 server 条目(current==total) → 直接 true，out = UpgradeSkills.Keys.First()
        // =========================================================================

        /// <summary>
        /// 剧本6、Rank=3：匹配到 6030101/6030201 两个剧本条件；
        /// server 中这两个条目均 current==total → 走剧本分支直接 true，out 为首个进化技能 key。
        /// </summary>
        [Fact]
        public void CanUpgrade_Scenario6_AllScenarioConditionsMet_ReturnsTrue()
        {
            var chara = MakeChara(
                talentLevel: 5,
                scenarioId: 6,
                // 两个剧本条件都完成；另给条件50一个未完成(0/1)条目，让下面的普通条件确实无法满足
                upgradeInfo: [Info(6030101, 1, 1), Info(6030201, 2, 2), Info(50, 0, 1)]);
            var talent = new TalentSkillData
            {
                Rank = 3,
                UpgradeSkills =
                {
                    // 普通条件设为真正不可能满足(条件50在server中未完成 + 没学到 Id=99999)，
                    // 证明返回 true 只能来自剧本分支而非普通分支
                    [300] = [new UpgradeCondition { ConditionId = 50, Type = UpgradeCondition.ConditionType.Specific, Requirement = 99999 }],
                },
            };

            Assert.True(talent.CanUpgrade(chara, out var upgraded, []));
            Assert.Equal(300, upgraded); // UpgradeSkills.Keys.First()
        }

        /// <summary>
        /// 剧本6、Rank=3：其中一个剧本条件(6030201)未完成 → 剧本分支的 All 不成立，
        /// 落入普通条件循环；普通条件同样不满足 → 整体 false。
        /// </summary>
        [Fact]
        public void CanUpgrade_Scenario6_ScenarioConditionUnmet_FallsThroughToFalse()
        {
            var chara = MakeChara(
                talentLevel: 5,
                scenarioId: 6,
                // 第二个剧本条件未完成(0/2)；条件50也给未完成条目，确保普通分支同样不满足
                upgradeInfo: [Info(6030101, 1, 1), Info(6030201, 0, 2), Info(50, 0, 1)]);
            var talent = new TalentSkillData
            {
                Rank = 3,
                UpgradeSkills =
                {
                    // 条件50在server中未完成(0/1)且没学到 Id=99999 → Specific 真正不满足
                    // （若用 server 中不存在的 condition_id，会被 IsArchived 的 null==null 短路误判为 true）
                    [301] = [new UpgradeCondition { ConditionId = 50, Type = UpgradeCondition.ConditionType.Specific, Requirement = 99999 }],
                },
            };

            Assert.False(talent.CanUpgrade(chara, out var upgraded, []));
            Assert.Equal(0, upgraded);
        }

        // =========================================================================
        // SkillData：Deconstruction / DisplayName / Clone
        // =========================================================================

        [Fact]
        public void SkillData_Deconstruction_ReturnsGroupRarityRate()
        {
            var skill = new SkillData { GroupId = 20001, Rarity = 2, Rate = 3, Name = "金右", Propers = [] };

            var tuple = skill.Deconstruction();
            Assert.Equal((20001, 2, 3), tuple);
        }

        [Fact]
        public void SkillData_DisplayName_FallsBackToNameThenOverrides()
        {
            var skill = new SkillData { Name = "原始名", Propers = [] };
            // 未设置翻译名时回退到 Name
            Assert.Equal("原始名", skill.DisplayName);

            skill.DisplayName = "翻译名";
            // 设置后优先用翻译名，Name 不变
            Assert.Equal("翻译名", skill.DisplayName);
            Assert.Equal("原始名", skill.Name);
        }

        [Fact]
        public void SkillData_Clone_IsShallowCopyWithIndependentInstance()
        {
            var original = new SkillData
            {
                Id = 1,
                GroupId = 20001,
                Rarity = 2,
                Rate = 2,
                Name = "原",
                Grade = 100,
                Cost = 50,
                Propers = [],
            };
            original.DisplayName = "原显示名";

            var clone = original.Clone();

            // 不是同一引用，但值逐字段相等
            Assert.NotSame(original, clone);
            Assert.Equal(original.Id, clone.Id);
            Assert.Equal(original.GroupId, clone.GroupId);
            Assert.Equal(original.Rarity, clone.Rarity);
            Assert.Equal(original.Rate, clone.Rate);
            Assert.Equal(original.Name, clone.Name);
            Assert.Equal(original.Grade, clone.Grade);
            Assert.Equal(original.Cost, clone.Cost);
            Assert.Equal("原显示名", clone.DisplayName); // 私有 translatedName 也被 MemberwiseClone 复制

            // 浅拷贝的实质:引用类型字段与原对象【共享同一实例】(MemberwiseClone 不递归复制)。
            // 这两条才真正区分深/浅——仅靠下面改值类型字段无法区分(值类型本就各自独立)。
            Assert.Same(original.Propers, clone.Propers);
            Assert.Same(original.Upgrades, clone.Upgrades);

            // 改克隆体的值类型属性不影响原对象(独立实例)
            clone.Grade = 999;
            Assert.Equal(100, original.Grade);
        }
    }
}
