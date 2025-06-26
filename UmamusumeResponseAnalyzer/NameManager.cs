using UmamusumeResponseAnalyzer.Entities;
using static UmamusumeResponseAnalyzer.Localization.NameManager;

namespace UmamusumeResponseAnalyzer
{
    public class NameManager
    {

        private static readonly BaseName nullBaseName = new(int.MinValue, I18N_Unknown);
        private static readonly SupportCardName nullSupportCardName = new(int.MinValue, I18N_Unknown, int.MinValue, int.MinValue);
        private static readonly UmaName nullUmaName = new(int.MinValue, I18N_Unknown, int.MinValue);
        private readonly Dictionary<int, BaseName> names;

        public NameManager(List<BaseName> data)
        {
            names = data.ToDictionary(x => x.Id, x => x);
            foreach (var i in names.Where(x => x.Value is SupportCardName))
            {
                i.Value.Nickname = $"{((SupportCardName)i.Value).TypeName}{i.Value.Nickname}";
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id">唯一ID，CharaId及CardId均可。</param>
        /// <returns>10x为各剧本的NPC<c>BaseName</c><br/>
        /// CharaId为人物<c>BaseName</c><br/>
        /// CardId则为S卡<c>SupportCardName</c>或角色<c>UmaName</c></returns>
        public string this[int id] => GetSimpleName(id);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="id">唯一ID，CharaId及CardId均可。</param>
        /// <returns>10x为各剧本的NPC<c>BaseName</c><br/>
        /// 其他则为人物<c>BaseName</c></returns>
        public BaseName GetCharacter(int id) => names.TryGetValue(id, out var value) ? value : nullBaseName;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0046:转换为条件表达式", Justification = "<挂起>")]
        public SupportCardName GetSupportCard(int id)
        {
            if (!names.TryGetValue(id, out var value)) return nullSupportCardName;
            if (value is not SupportCardName) throw new Exception(string.Format(I18N_CastToSupportCardNameFail, value.GetType()));
            return (SupportCardName)value;
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0046:转换为条件表达式", Justification = "<挂起>")]
        public UmaName GetUmamusume(int id)
        {
            if (!names.TryGetValue(id, out var value)) return nullUmaName;
            if (value is not UmaName) throw new Exception(string.Format(I18N_CastToUmaNameFail, value.GetType()));
            return (UmaName)value;
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0046:转换为条件表达式", Justification = "<挂起>")]
        private string GetSimpleName(int id)
        {
            if (!names.TryGetValue(id, out var value)) return I18N_Unknown;
            return value switch
            {
                SupportCardName scn => scn.SimpleName,
                UmaName un => un.CharacterName,
                _ => value.Name,
            };
        }
        //找到对应角色的r卡种类
        public int GetRSupportCardTypeByCharaId(int charaId)
        {
            if (!names.Values
                .OfType<SupportCardName>()
                .Any(card => card.Id >= 10000 && card.Id <= 12000 && card.CharaId == charaId))
                return -1;
            var matchingCards = names.Values
                .OfType<SupportCardName>()
                .Where(card => card.Id >= 10000 && card.Id <= 12000 && card.CharaId == charaId)
                .ToList();

            if (matchingCards.Count == 0)
            {
                //throw new Exception($"未找到CharaId为{charaId}且ID介于10000和12000之间的SupportCardName。");
                return -1;
            }
            else if (matchingCards.Count > 1)
            {
                throw new Exception($"找到多个CharaId为{charaId}且ID介于10000和12000之间的SupportCardName。");
            }

            return matchingCards[0].Type;
        }
    }
}
