using Gallop;
using Spectre.Console;
using System.Collections.Frozen;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class TrainingPartner
    {
        static readonly FrozenDictionary<int, int> ToTrainId = new Dictionary<int, int>
        {
            [1101] = 101,
            [1102] = 105,
            [1103] = 102,
            [1104] = 103,
            [1105] = 106,
            [601] = 101,
            [602] = 105,
            [603] = 102,
            [604] = 103,
            [605] = 106,
            [101] = 101,
            [105] = 105,
            [102] = 102,
            [103] = 103,
            [106] = 106,
            [2101] = 101,
            [2201] = 101,
            [2301] = 101,
            [2102] = 105,
            [2202] = 105,
            [2302] = 105,
            [2103] = 102,
            [2203] = 102,
            [2303] = 102,
            [2104] = 103,
            [2204] = 103,
            [2304] = 103,
            [2105] = 106,
            [2205] = 106,
            [2305] = 106,
            [901] = 101,
            [902] = 102,
            [906] = 106
        }.ToFrozenDictionary();
        public PartnerPriority Priority { get; private set; } = PartnerPriority.默认;
        /// <summary>
        /// 该卡在卡组中的位置(从0开始)
        /// </summary>
        public int Position { get; }
        public int CardId { get; }
        public string Name { get; }
        public int Friendship { get; }
        public bool IsNpc => Position is not (>= 1 and <= 6);
        public string NameColor { get; } = "[#ffffff]";
        public string NameAppend { get; } = string.Empty;
        public bool Shining { get; } = false;

        public TrainingPartner(TurnInfo turn, int partner, SingleModeCommandInfo command, IDictionary<int, int> toTrainIdDictionary = null!)
        {
            Position = partner;
            Friendship = turn.Evaluations[Position].evaluation;
            if (!IsNpc) // 自己带的S卡
            {
                CardId = turn.SupportCards[Position];
                Name = Database.Names.GetSupportCard(CardId).Nickname.EscapeMarkup();
                if (Name.Contains("[友]")) // 友人单独标绿
                {
                    Priority = PartnerPriority.友人;
                    NameColor = $"[green]";

                }
                else if (Friendship < 80)// 除了友人以外都可以进行友情训练，检测羁绊
                {
                    Priority = PartnerPriority.羁绊不足;
                    NameColor = "[yellow]";
                }
                //在得意位置上
                Shining = Friendship >= 80 &&
                    Name.Contains((toTrainIdDictionary ?? ToTrainId)[command.command_id] switch
                    {
                        101 => "[速]",
                        105 => "[耐]",
                        102 => "[力]",
                        103 => "[根]",
                        106 => "[智]",
                    });

                if ((CardId == 30137 && turn.GetCommonResponse().chara_info.chara_effect_id_array.Any(x => x == 102)) || //神团
                (CardId == 30067 && turn.GetCommonResponse().chara_info.chara_effect_id_array.Any(x => x == 101)) || //皇团
                (CardId == 30081 && turn.GetCommonResponse().chara_info.chara_effect_id_array.Any(x => x == 100)) //天狼星
                )
                {
                    Shining = true;
                    NameColor = $"[#80ff00]";
                }

                if (Shining)
                {
                    if (Name.Contains("[友]"))
                    {
                        Priority = PartnerPriority.友人;
                        NameColor = $"[#80ff00]";
                    }
                    else
                    {
                        Priority = PartnerPriority.闪;
                        NameColor = $"[aqua]";
                    }
                }
            }
            else // NPC
            {
                Name = (Database.Names.GetCharacter(Position).Nickname).EscapeMarkup();
                if (Position is >= 100 and < 1000) // 理事长、记者等
                {
                    Priority = PartnerPriority.关键NPC;
                    NameColor = $"[#008080]";
                }
            }

            // 自己带的支援卡，或理事长、记者、佐岳等
            if (Position is >= 1 and <= 7 or >= 100 and < 1000)
            {
                // 羁绊不满，额外显示
                if (Friendship < 100)
                {
                    NameAppend += $"[red]{Friendship}[/]";
                }
            }

            Name = $"{NameColor}{Name}[/]{NameAppend}";
            var tips = command.tips_event_partner_array.Intersect(command.training_partner_array);
            if (tips.Contains(Position)) // 有Hint就加个红感叹号，和游戏内表现一样
                Name = $"[red]![/]{Name}";
        }
    }
}
