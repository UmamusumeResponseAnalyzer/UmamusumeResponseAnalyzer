using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer
{
    public class SkillManager
    {
        readonly Dictionary<int, SkillData> idMap;
        readonly Dictionary<(int GroupId, int Rarity, int Rate), SkillData> rateMap;
        readonly Dictionary<(int GroupId, int Rarity), SkillData[]> rarityMap;

        public SkillManager(List<SkillData> list)
        {
            idMap = list.ToDictionary(x => x.Id, x => x);
            rateMap = list.ToDictionary(x => (x.GroupId, x.Rarity, x.Rate), x => x);
            rarityMap = list.GroupBy(x => (x.GroupId, x.Rarity)).ToDictionary(x => x.Key, x => x.ToArray());
        }
        public SkillData this[(int GroupId, int Rarity, int Rate) tuple]
        {
            get => rateMap.ContainsKey(tuple) ? rateMap[tuple] : null!;
            set => rateMap[tuple] = value;
        }
        /// <summary>
        /// 根据GroupId和Rarity获得所有同类技能(通常是单圈双圈绿)
        /// </summary>
        /// <param name="tuple">技能的GroupId、Rarity</param>
        /// <returns>所有具有相同GroupId、Rarity的技能</returns>
        public SkillData[] this[(int GroupId, int Rarity) tuple]
        {
            get => rarityMap.ContainsKey(tuple) ? rarityMap[tuple] : null!;
            set => rarityMap[tuple] = value;
        }
        public SkillData this[int Id]
        {
            get => idMap.ContainsKey(Id) ? idMap[Id] : null!;
            set => idMap[Id] = value;
        }
        public (int GroupId, int Rarity, int Rate) Deconstruction(int Id) => this[Id].Deconstruction();
        /// <summary>
        /// 获得某个技能的所有子技能(金、双圈、单圈、×)
        /// </summary>
        /// <param name="groupId">技能的GroupId</param>
        /// <returns>所有具有相同GroupId的技能</returns>
        public SkillData[] GetAllByGroupId(int groupId) => idMap.Where(x => x.Value.GroupId == groupId).Select(x => x.Value).ToArray();

    }
}
