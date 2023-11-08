using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class BaseName
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public T Cast<T>() where T : BaseName
        {
            return (T)this;
        }

        public BaseName(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }
    public class SupportCardName : BaseName
    {
        public int CharaId { get; set; }
        public int Type { get; set; }
        public string CharacterName => Database.Names[CharaId].Name;
        public string TypeName => Type switch { 101 => "[速]", 102 => "[力]", 103 => "[根]", 105 => "[耐]", 106 => "[智]", 0 => "[友]", _ => "" };
        public string FullName => $"{Name}{CharacterName}";
        public string SimpleName => $"{TypeName}{CharacterName}";

        public SupportCardName(int id, string name, int type, int charaId) : base(id, name)
        {
            Type = type;
            CharaId = charaId;
        }
    }
    public class UmaName : BaseName
    {
        public int CharaId { get; set; }
        public string CharacterName => Database.Names[CharaId].Name;
        public string FullName => $"{Name}{CharacterName}";
        public UmaName(int id, string name) : base(id, name)
        {
            CharaId = int.Parse(id.ToString()[0] == '9' ? id.ToString()[1..5] : id.ToString()[..4]);
        }
    }
}
