using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class SkillData
    {
        private int _realCost = -1;
        private int _realGrade = int.MinValue;
        /// <summary>
        /// 此技能的上位技能,不计算hint及适性,需调用Apply单独应用
        /// </summary>
        public SkillData Superior => Database.Skills[(GroupId, Rarity, Rate + 1)] ?? Database.Skills[(GroupId, Rarity + 1, Rate + 1)];
        /// <summary>
        /// 此技能的下位技能,不计算hint及适性,需调用Apply单独应用
        /// </summary>
        public SkillData Inferior => Database.Skills[(GroupId, Rarity, Rate - 1)] ?? Database.Skills[(GroupId, Rarity - 1, Rate - 1)];
        /// <summary>
        /// backing field
        /// </summary>
        private int _totalCost = 0;
        /// <summary>
        /// 学会该技能所需的总技能点（包括下位技能，不计算折扣）
        /// </summary>
        public int TotalCost
        {
            get
            {
                if (_totalCost != 0) return _totalCost;
                var total = Cost;
                var inferior = Inferior;
                while (inferior != null && inferior.Rate > 0)
                {
                    total += inferior.Cost;
                    inferior = inferior.Inferior;
                }
                return total;
            }
            set => _totalCost = value;
        }
        public string Name;
        public int Id;
        public int GroupId;
        public int Rarity;
        public int Rate;
        public int Grade;
        public int Cost;
        public int DisplayOrder;
        public SkillProper[] Propers;

        /// <summary>
        /// 根据马的属性应用折扣，改变技能的价格
        /// </summary>
        /// <param name="chara_info">@event.data.chara_info</param>
        /// <param name="level">该技能的折扣等级</param>
        /// <returns></returns>
        private void ApplyHint(Gallop.SingleModeChara chara_info, int level)
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
            Cost = Cost * (100 - off - cutted) / 100;
        }
        /// <summary>
        /// 根据马的属性应用相性加成，改变技能的分数
        /// </summary>
        /// <param name="chara_info">@event.data.chara_info</param>
        private void ApplyProper(Gallop.SingleModeChara chara_info)
        {
            // 仅在技能有触发条件时应用，假设通用技能分数固定不变
            if (Propers.Any())
            {
                Grade = Propers.Max(i =>
                {
                    var grade = Grade;
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
        /// <summary>
        /// 应用折扣及适性加成，返回的是一个新的对象
        /// </summary>
        /// <param name="chara_info"></param>
        /// <param name="level"></param>
        /// <returns>新的对象！！记得赋值</returns>
        public SkillData Apply(Gallop.SingleModeChara chara_info, int level = int.MinValue)
        {
            if (level == int.MinValue)
            {
                //自动搜索hint level?
                level = chara_info.skill_tips_array.FirstOrDefault(x => x.group_id == GroupId && x.rarity == Rarity)?.level ?? 0;
            }
            var instance = Clone();
            instance.ApplyHint(chara_info, level);
            instance.ApplyProper(chara_info);
            return instance;
        }
        /// <summary>
        /// 扣除已买技能的开销后的实际价格
        /// </summary>
        /// <param name="chara_info">角色信息</param>
        /// <returns></returns>
        public int GetRealCost(Gallop.SingleModeChara chara_info = null!)
        {
            if (chara_info is null || _realCost != -1)
            {
                if (_realCost == -1)
                    throw new Exception("在未计算GetRealCost前就尝试读取RealCost");
                else
                    return _realCost;
            }
            if (chara_info.skill_array.Any(x => x.skill_id == Id))
                _realCost = 0;
            else if (Inferior == null)
                _realCost = Cost;
            else
                _realCost = Cost + Inferior.Apply(chara_info).GetRealCost(chara_info);
            return _realCost;
        }
        /// <summary>
        /// 扣除已买技能的开销后的实际分数
        /// </summary>
        /// <param name="chara_info">角色信息</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public int GetRealGrade(Gallop.SingleModeChara chara_info = null!)
        {
            if (chara_info is null || _realGrade != int.MinValue)
            {
                if (_realGrade == int.MinValue)
                    throw new Exception("在未计算GetRealGrade前就尝试读取RealGrade");
                else
                    return _realGrade;
            }
            if (chara_info.skill_array.Any(x => x.skill_id == Id))
                _realGrade = 0;
            else if (Inferior == null)
                _realGrade = Grade;
            else
                _realGrade = Grade - Inferior.Apply(chara_info).Grade + Inferior.Apply(chara_info).GetRealGrade(chara_info);
            return _realGrade;
        }
        public (int GroupId, int Rarity, int Rate) Deconstruction() => (GroupId, Rarity, Rate);
        public SkillData Clone()
            => new()
            {
                Name = Name,
                Id = Id,
                GroupId = GroupId,
                Rarity = Rarity,
                Rate = Rate,
                Grade = Grade,
                Cost = Cost,
                DisplayOrder = DisplayOrder,
                Propers = Propers
            };

        public class SkillProper
        {
            public GroundType Ground { get; set; } = GroundType.None;
            public DistanceType Distance { get; set; } = DistanceType.None;
            public StyleType Style { get; set; } = StyleType.None;

            public enum GroundType
            {
                None,
                Turf,
                Dirt
            }
            public enum DistanceType
            {
                None,
                Short,
                Mile,
                Middle,
                Long
            }
            public enum StyleType
            {
                None,
                Nige,
                Senko,
                Sashi,
                Oikomi
            }
        }
    }
    public class TalentSkillData
    {
        public int SkillId;
        public int Rank;
        public Dictionary<int, int[]> UpgradeSkills = new();
    }
}
