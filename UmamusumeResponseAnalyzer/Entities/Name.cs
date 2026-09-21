using Newtonsoft.Json;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class BaseName(int id, string name, string nickname)
    {
        /// <summary>
        /// 角色ID，通常为4位数字，且马娘均为1xxx
        /// </summary>
        public int Id { get; init; } = id;
        /// <summary>
        /// 角色的本名，如美浦波旁
        /// </summary>
        public string Name { get; init; } = name;
        /// <summary>
        /// 长度限定为2汉字的简称，如美浦波旁=>波旁
        /// </summary>
        public string Nickname { get; init; } = nickname;
    }
    public class SupportCardName(int id, string name, string nickname, int type, int charaId) : BaseName(id, name, nickname)
    {
        /// <summary>
        /// 支援卡ID
        /// </summary>
        public int CharaId { get; init; } = charaId;
        /// <summary>
        /// 支援卡的类型（速耐力根智友团）
        /// </summary>
        public int Type { get; init; } = type;
        /// <summary>
        /// 角色的本名，如美浦波旁
        /// </summary>
        [JsonIgnore]
        public string CharacterName => Database.Names.DisplayName(CharaId);
        /// <summary>
        /// 支援卡的类型(如[速])
        /// </summary>
        [JsonIgnore]
        public string TypeName => $"[{Type switch
        {
            101 => Localization.Game.I18N_SpeedSimple,
            102 => Localization.Game.I18N_PowerSimple,
            103 => Localization.Game.I18N_NutsSimple,
            105 => Localization.Game.I18N_StaminaSimple,
            106 => Localization.Game.I18N_WizSimple,
            0 => Localization.Game.I18N_FriendSimple,
            _ => Type.ToString()
        }}]";
        [JsonIgnore]
        public bool IsFriendCard => Type == 0;
        public bool CanTriggerFriendshipTraining(int trainingType) => Type == trainingType || Id is 30241;
        /// <summary>
        /// 支援卡的全名，如[ミッション『心の栄養補給』] ミホノブルボン
        /// </summary>
        [JsonIgnore]
        public string FullName => $"{Name}{CharacterName}";
        /// <summary>
        /// 支援卡的简称，如[智]波旁，不考虑同类型同马娘支援卡的区分
        /// </summary>
        [JsonIgnore]
        public string SimpleName => $"{TypeName}{CharacterName}";
    }

    public class UmaName(int id, string name, string nickname, int charaId = 0) : BaseName(id, name, nickname)
    {
        /// <summary>
        /// 马娘ID
        /// </summary>
        public int CharaId { get; init; } = charaId == 0 ? int.Parse(id.ToString()[0] == '9' ? id.ToString()[1..5] : id.ToString()[..4]) : charaId;
        /// <summary>
        /// 马娘的本名，如美浦波旁
        /// </summary>
        [JsonIgnore]
        public string CharacterName => Database.Names.DisplayName(CharaId);
        /// <summary>
        /// 马娘的全名，如[CODE：グラサージュ] ミホノブルボン
        /// </summary>
        [JsonIgnore]
        public string FullName => $"{Name}{CharacterName}";
    }
}
