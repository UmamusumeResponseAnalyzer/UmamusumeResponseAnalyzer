using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer
{
    public class NameManager
    {

        private static readonly BaseName nullBaseName = new(int.MinValue, "未知");
        private static readonly SupportCardName nullSupportCardName = new(int.MinValue, "未知", int.MinValue, int.MinValue);
        private static readonly UmaName nullUmaName = new(int.MinValue, "未知", int.MinValue);
        private readonly Dictionary<int, BaseName> names;

        public NameManager(List<BaseName> data)
        {
            names = data.ToDictionary(x => x.Id, x => x);
            foreach (var i in names.Where(x => x.Value is SupportCardName))
            {
                i.Value.Nickname = $"{((SupportCardName)i.Value).TypeName}{i.Value.Nickname}";
            }
        }

        public string this[int id] => GetSimpleName(id);
        public BaseName GetCharacter(int id) => names.ContainsKey(id) ? names[id] : nullBaseName;
        public SupportCardName GetSupportCard(int id)
        {
            if (!names.ContainsKey(id)) return nullSupportCardName;
            if (names[id] is not SupportCardName) throw new Exception($"无法从{names[id].GetType()}转换到SupportCardName");
            return (SupportCardName)names[id];
        }
        public UmaName GetUmamusume(int id)
        {
            if (!names.ContainsKey(id)) return nullUmaName;
            if (names[id] is not UmaName) throw new Exception($"无法从{names[id].GetType()}转换到UmaName");
            return (UmaName)names[id];
        }
        private string GetSimpleName(int id)
        {
            if (!names.ContainsKey(id)) return "未知";
            return names[id] switch
            {
                SupportCardName scn => scn.SimpleName,
                UmaName un => un.CharacterName,
                _ => names[id].Name,
            };
        }
    }
}
