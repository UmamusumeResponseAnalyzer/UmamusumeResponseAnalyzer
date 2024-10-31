using Gallop;
using MathNet.Numerics.RootFinding;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class TrainingPartner
    {
        public PartnerPriority Priority { get; private set; } = PartnerPriority.默认;
        /// <summary>
        /// 该卡在卡组中的位置(从0开始)
        /// </summary>
        public int Position { get; }
        public int CardId { get; }
        public string Name { get; }
        public int Friendship { get; }
        public bool IsArcPartner { get; }
        public bool IsNpc => Position is not (>= 1 and <= 6);
        public string NameColor { get; } = "[#ffffff]";
        public string NameAppend { get; } = string.Empty;
        public bool Shining { get; } = false;

        public TrainingPartner(TurnInfo turn, int partner, SingleModeCommandInfo command)
        {
            Position = partner;
            Friendship = turn.Evaluations[Position].evaluation;
            IsArcPartner = turn.IsScenario(ScenarioType.LArc, out TurnInfoArc arcTurn) && (partner is > 1000 || (partner is >= 1 and <= 7)) && arcTurn.EvaluationInfoArray.Any(x => x.target_id == partner);
            if (!IsNpc) // 自己带的S卡
            {
                CardId = turn.SupportCards[Position];
                var turnStat = GameStats.stats[turn.Turn];
                if (turnStat != null)   // UAF这里没有初始化,跳过
                {
                    var trainIdx = GameGlobal.ToTrainIndex[command.command_id];
                    switch (CardId)
                    {
                        case 30137: // 三女神团队卡的友情训练
                            turnStat.venus_venusTrain = GameGlobal.ToTrainId[command.command_id];
                            break;
                        case 30160 or 10094: // 佐岳友人卡
                            turnStat.larc_zuoyueAtTrain[trainIdx] = true;
                            break;
                        case 30188 or 10104:    // 都留岐涼花
                            turnStat.uaf_friendAtTrain[trainIdx] = true;
                            break;
                        case 30207 or 10109:    // 理事长
                            turnStat.cook_friendAtTrain[trainIdx] = true;
                            break;
                    }
                }
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
                    Name.Contains(GameGlobal.ToTrainId[command.command_id] switch
                    {
                        101 => "[速]",
                        105 => "[耐]",
                        102 => "[力]",
                        103 => "[根]",
                        106 => "[智]",
                    });
                //GM杯检查
                if (turn.IsScenario(ScenarioType.GrandMasters) && turn.GetCommonResponse().venus_data_set.venus_spirit_active_effect_info_array.Any(x => x.chara_id == 9042 && x.effect_group_id == 421)
                && (Name.Contains("[速]") || Name.Contains("[耐]") || Name.Contains("[力]") || Name.Contains("[根]") || Name.Contains("[智]")))
                {
                    Shining = true;
                }

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
                    //LArcShiningCount[GameGlobal.ToTrainIndex[scenarioCommandId]] += 1;
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
                else if (IsArcPartner) // 凯旋门的其他人
                {
                    Priority = PartnerPriority.无用NPC;
                    NameColor = $"[#a166ff]";
                }
            }

            // 自己带的支援卡，或理事长、记者、佐岳等
            if (Position is >= 1 and <= 7 || Position is >= 100 and < 1000)
            {
                // 羁绊不满，额外显示
                if (Friendship < 100)
                {
                    NameAppend += $"[red]{Friendship}[/]";
                }
            }

            //if (isArcPartner && !arcTurn.IsAbroad)
            //{
            //    var chara_id = @event.data.arc_data_set.evaluation_info_array.First(x => x.target_id == partner).chara_id;
            //    if (@event.data.arc_data_set.arc_rival_array.Any(x => x.chara_id == chara_id))
            //    {
            //        var arc_data = @event.data.arc_data_set.arc_rival_array.First(x => x.chara_id == chara_id);
            //        var rival_boost = arc_data.rival_boost;
            //        var effectId = arc_data.selection_peff_array.First(x => x.effect_num == arc_data.selection_peff_array.Min(x => x.effect_num)).effect_group_id;
            //        if (rival_boost != 3)
            //        {
            //            if (priority > PartnerPriority.需要充电) priority = PartnerPriority.需要充电;
            //            LArcRivalBoostCount[trainIdx, rival_boost] += 1;
            //
            //            if (partner > 1000)
            //                nameColor = $"[#ff00ff]";
            //            nameAppend += $":[aqua]{rival_boost}[/]{GameGlobal.LArcSSEffectNameColoredShort[effectId]}";
            //        }
            //    }
            //}
            Name = $"{NameColor}{Name}[/]{NameAppend}";
            var tips = command.tips_event_partner_array.Intersect(command.training_partner_array);
            if (tips.Contains(Position)) // 有Hint就加个红感叹号，和游戏内表现一样
                Name = $"[red]![/]{Name}";
        }
    }
}
