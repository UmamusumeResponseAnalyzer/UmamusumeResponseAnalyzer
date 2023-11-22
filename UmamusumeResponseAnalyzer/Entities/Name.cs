using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class BaseName(int id, string name)
    {
        /// <summary>
        /// 角色ID，通常为4位数字，且马娘均为1xxx
        /// </summary>
        public int Id { get; set; } = id;
        /// <summary>
        /// 角色的本名，如美浦波旁
        /// </summary>
        public string Name { get; set; } = name;
        /// <summary>
        /// 长度限定为2汉字的简称，如美浦波旁=>波旁
        /// </summary>
        public string Nickname { get; set; } = "未知";
    }
    public class SupportCardName(int id, string name, int type, int charaId) : BaseName(id, name)
    {
        /// <summary>
        /// 支援卡ID
        /// </summary>
        public int CharaId { get; set; } = charaId;
        /// <summary>
        /// 支援卡的类型（速耐力根智友团）
        /// </summary>
        public int Type { get; set; } = type;
        /// <summary>
        /// 角色的本名，如美浦波旁
        /// </summary>
        public string CharacterName => Database.Names[CharaId];
        /// <summary>
        /// 支援卡的类型(如[速])
        /// </summary>
        public string TypeName => Type switch { 101 => "[速]", 102 => "[力]", 103 => "[根]", 105 => "[耐]", 106 => "[智]", 0 => "[友]", _ => "" };
        /// <summary>
        /// 支援卡的全名，如[ミッション『心の栄養補給』] ミホノブルボン
        /// </summary>
        public string FullName => $"{Name}{CharacterName}";
        /// <summary>
        /// 支援卡的简称，如[智]波旁，不考虑同类型同马娘支援卡的区分
        /// </summary>
        public string SimpleName => $"{TypeName}{CharacterName}";
    }

    // int.MinValue被未知占用，所以charaId的默认值是0
    public class UmaName(int id, string name, int charaId = 0) : BaseName(id, name)
    {
        /// <summary>
        /// 马娘ID
        /// </summary>
        public int CharaId { get; set; } = charaId == 0 ? int.Parse(id.ToString()[0] == '9' ? id.ToString()[1..5] : id.ToString()[..4]) : charaId;
        /// <summary>
        /// 马娘的本名，如美浦波旁
        /// </summary>
        public string CharacterName => Database.Names[CharaId];
        /// <summary>
        /// 马娘的全名，如[CODE：グラサージュ] ミホノブルボン
        /// </summary>
        public string FullName => $"{Name}{CharacterName}";
    }
}
