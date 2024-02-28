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
        public BaseName GetCharacter(int id) => names.TryGetValue(id, out BaseName? value) ? value : nullBaseName;
        public SupportCardName GetSupportCard(int id)
        {
            if (!names.TryGetValue(id, out BaseName? value)) return nullSupportCardName;
            if (value is not SupportCardName) throw new Exception(string.Format(I18N_CastToSupportCardNameFail, value.GetType()));
            return (SupportCardName)value;
        }
        public UmaName GetUmamusume(int id)
        {
            if (!names.TryGetValue(id, out BaseName? value)) return nullUmaName;
            if (value is not UmaName) throw new Exception(string.Format(I18N_CastToUmaNameFail, value.GetType()));
            return (UmaName)value;
        }
        private string GetSimpleName(int id)
        {
            if (!names.TryGetValue(id, out BaseName? value)) return I18N_Unknown;
            return value switch
            {
                SupportCardName scn => scn.SimpleName,
                UmaName un => un.CharacterName,
                _ => value.Name,
            };
        }
    }
}
