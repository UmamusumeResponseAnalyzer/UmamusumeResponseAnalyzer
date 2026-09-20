using Gallop;
using System.Collections.Frozen;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class TrainingPartner
    {
        static readonly int[] TrainIds = [101, 105, 102, 103, 106];
        static readonly FrozenDictionary<int, int> ToTrainId = CommandInfo.ToTrainIndex
            .ToFrozenDictionary(x => x.Key, x => TrainIds[x.Value]);
        public PartnerPriority Priority { get; private set; } = PartnerPriority.默认;
        /// <summary>
        /// 该卡在卡组中的位置(从0开始)
        /// </summary>
        public int Position { get; }
        public int CardId { get; }
        public string Name { get; }
        public int Friendship { get; }
        public bool IsNpc => Position is not (>= 1 and <= 6);
        public string NameAppend { get; } = string.Empty;
        public bool Shining { get; } = false;

        public TrainingPartner(TurnInfo turn, int partner, SingleModeCommandInfo command, IDictionary<int, int> toTrainIdDictionary = null!)
        {
            Position = partner;
            Friendship = turn.Evaluations[Position].evaluation;
            if (!IsNpc) // 自己带的S卡
            {
                CardId = turn.SupportCards[Position];
                var supportCard = Database.Names.GetRequiredSupportCard(CardId);
                var isFriendSupportCard = supportCard.IsFriendCard;
                Name = Database.Names.DisplayNickname(CardId);
                if (isFriendSupportCard) // 友人单独标绿
                {
                    Priority = PartnerPriority.友人;
                }
                else if (Friendship < 80)// 除了友人以外都可以进行友情训练，检测羁绊
                {
                    Priority = PartnerPriority.羁绊不足;
                }
                //在得意位置上
                Shining = Friendship >= 80
                    && supportCard.CanTriggerFriendshipTraining((toTrainIdDictionary ?? ToTrainId)[command.command_id]);

                if ((CardId == 30137 && turn.GetCommonResponse().chara_info.chara_effect_id_array.Contains(102)) //神团
                    || (CardId == 30067 && turn.GetCommonResponse().chara_info.chara_effect_id_array.Contains(101)) //皇团
                    || (CardId == 30081 && turn.GetCommonResponse().chara_info.chara_effect_id_array.Contains(100))) //天狼星
                {
                    Shining = true;
                }

                if (Shining)
                {
                    if (isFriendSupportCard)
                    {
                        Priority = PartnerPriority.友人;
                    }
                    else
                    {
                        Priority = PartnerPriority.闪;
                    }
                }
            }
            else // NPC
            {
                Name = Database.Names.DisplayNickname(Position);
                if (Position is >= 100 and < 1000) // 理事长、记者等
                {
                    Priority = PartnerPriority.关键NPC;
                }
            }

            // 自己带的支援卡，或理事长、记者、佐岳等
            if (Position is >= 1 and <= 7 or >= 100 and < 1000)
            {
                // 羁绊不满，额外显示
                if (Friendship < 100)
                {
                    NameAppend += Friendship;
                }
            }

            Name += NameAppend;
            var tips = command.tips_event_partner_array.Intersect(command.training_partner_array);
            if (tips.Contains(Position)) // 有Hint就加个红感叹号，和游戏内表现一样
                Name = $"!{Name}";
        }
    }
}
