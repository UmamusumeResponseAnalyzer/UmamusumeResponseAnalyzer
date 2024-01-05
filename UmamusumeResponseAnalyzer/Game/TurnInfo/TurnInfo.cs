using Gallop;
using System.Collections.Frozen;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer.Game.TurnInfo
{
    public class TurnInfo
    {
        /// <summary>
        /// 马娘ID
        /// </summary>
        public int CharacterId => int.Parse(CardId.ToString()[..4]);
        public int SpeedRevised => ScoreUtils.ReviseOver1200(Speed);
        public int StaminaRevised => ScoreUtils.ReviseOver1200(Stamina);
        public int PowerRevised => ScoreUtils.ReviseOver1200(Power);
        public int WizRevised => ScoreUtils.ReviseOver1200(Wiz);
        public int GutsRevised => ScoreUtils.ReviseOver1200(Guts);
        public int[] Stats => [Speed, Stamina, Power, Wiz, Guts];
        public int[] StatsRevised => [SpeedRevised, StaminaRevised, PowerRevised, WizRevised, GutsRevised];
        public int[] MaxStatsRevised => [ScoreUtils.ReviseOver1200(MaxSpeed), ScoreUtils.ReviseOver1200(MaxStamina), ScoreUtils.ReviseOver1200(MaxPower), ScoreUtils.ReviseOver1200(MaxWiz), ScoreUtils.ReviseOver1200(MaxGuts)];
        public int TotalStats => StatsRevised.Sum();
        public int Year => (Turn - 1) / 24 + 1;
        public int Month => ((Turn - 1) % 24) / 2 + 1;
        public string HalfMonth => (Turn % 2 == 0) ? "后半" : "前半";
        public int TotalTurns = 78;
        public bool IsFreeContinueAvailable => FreeContinueTime < DateTimeOffset.Now.ToUnixTimeSeconds();
        #region chara_info
        /// <summary>
        /// 账号中的独特育成ID
        /// </summary>
        public int SingleModeCharaId { get; }
        /// <summary>
        /// 决胜服ID
        /// </summary>
        public int CardId { get; }
        /// <summary>
        /// 未知
        /// </summary>
        public int CharaGrade { get; }
        /// <summary>
        /// 速度
        /// </summary>
        public int Speed { get; }
        /// <summary>
        /// 耐力
        /// </summary>
        public int Stamina { get; }
        /// <summary>
        /// 力量
        /// </summary>
        public int Power { get; }
        /// <summary>
        /// 根性
        /// </summary>
        public int Wiz { get; }
        /// <summary>
        /// 智力
        /// </summary>
        public int Guts { get; }
        /// <summary>
        /// 体力
        /// </summary>
        public int Vital { get; }
        /// <summary>
        /// 最大速度
        /// </summary>
        public int MaxSpeed { get; }
        /// <summary>
        /// 最大耐力
        /// </summary>
        public int MaxStamina { get; }
        /// <summary>
        /// 最大力量
        /// </summary>
        public int MaxPower { get; }
        /// <summary>
        /// 最大根性
        /// </summary>
        public int MaxWiz { get; }
        /// <summary>
        /// 最大智力
        /// </summary>
        public int MaxGuts { get; }
        /// <summary>
        /// 最大体力
        /// </summary>
        public int MaxVital { get; }
        /// <summary>
        /// 干劲 <br/>
        /// 5-绝好调,4-好调,3-普通,2-不调,1-绝不调
        /// </summary>
        public int Motivation { get; }
        /// <summary>
        /// 粉丝数
        /// </summary>
        public int Fans { get; }
        /// <summary>
        /// 决胜服星级
        /// </summary>
        public int Rarity { get; }
        /// <summary>
        /// 比赛用跑法
        /// </summary>
        public StyleType RaceRunningStyle { get; }
        /// <summary>
        /// 场地适性，Key为GroundType,Value为int <br/>
        /// 8-S,7-A,6-B,5-C,4-D,3-E,2-F,1-G
        /// </summary>
        public FrozenDictionary<GroundType, int> GroundPropers { get; }
        /// <summary>
        /// 距离适性，Key为DistanceType,Value为int <br/>
        /// 8-S,7-A,6-B,5-C,4-D,3-E,2-F,1-G
        /// </summary>
        public FrozenDictionary<DistanceType, int> DistancePropers { get; }
        /// <summary>
        /// 跑法适性，Key为StyleType,Value为int <br/>
        /// 8-S,7-A,6-B,5-C,4-D,3-E,2-F,1-G
        /// </summary>
        public FrozenDictionary<StyleType, int> StylePropers { get; }
        /// <summary>
        /// 天赋等级
        /// </summary>
        public int TalentLevel { get; }
        /// <summary>
        /// 已学习技能
        /// </summary>
        public (int SkillId, int Level)[] SkillArray { get; }
        /// <summary>
        /// 未知
        /// </summary>
        public int[] DisableSkillIdArray { get; }
        /// <summary>
        /// 可学习技能
        /// </summary>
        public (int GroupId, int Rarity, int Level)[] SkillTipsArray { get; }
        public (int Position, int SupportCardId, int LimitBreakCount, int Exp, long OwnerViewerId)[] SupportCardArray { get; }
        /// <summary>
        /// 父种马的SingleModeCharaId
        /// </summary>
        public int SuccessionTrainedCharaId1 { get; }
        /// <summary>
        /// 母种马的SingleModeCharaId
        /// </summary>
        public int SuccessionTrainedCharaId2 { get; }
        /// <summary>
        /// 回合数
        /// </summary>
        public int Turn { get; }
        /// <summary>
        /// 技能点
        /// </summary>
        public int SkillPoint { get; }
        /// <summary>
        /// 育成状态，细节未知
        /// </summary>
        public int State { get; }
        /// <summary>
        /// 未知
        /// </summary>
        public int PlayingState { get; }
        /// <summary>
        /// 剧本ID
        /// </summary>
        public ScenarioType Scenario { get; }
        /// <summary>
        /// 育成开始时间
        /// </summary>
        public DateTimeOffset StartTime { get; }
        public EvaluationInfo[] EvaluationInfoArray { get; }
        public int[] CharaEffectIdArray { get; }
        public (int ConditionId, int TotalCount, int CurrentCount)[] SkillUpgradeInfoArray { get; }
        #endregion
        #region home_info
        /// <summary>
        /// 训练信息
        /// </summary>
        public CommandInfo[] CommandInfoArray { get; }
        /// <summary>
        /// 是否禁止参加比赛？存疑
        /// </summary>
        public int RaceEntryRestriction { get; }
        /// <summary>
        /// 不可用训练
        /// </summary>
        public int[] DisableCommandIdArray { get; }
        /// <summary>
        /// 剩余可用闹钟
        /// </summary>
        public int AvailableContinueNum { get; }
        /// <summary>
        /// 剩余可用免费闹钟
        /// </summary>
        public int AvailableFreeContinueNum { get; }
        /// <summary>
        /// 免费闹钟总数
        /// </summary>
        public int FreeContinueNum { get; }
        /// <summary>
        /// 下一次免费闹钟可用时的时间戳
        /// </summary>
        public int FreeContinueTime { get; }
        #endregion
        #region unchecked_event_array
        public UncheckedEvent[] UncheckedEventArray { get; }
        #endregion

        public TurnInfo(SingleModeCheckEventResponse ev)
        {
            #region chara_info
            var chara_info = ev.data.chara_info;
            SingleModeCharaId = chara_info.single_mode_chara_id;
            CardId = chara_info.card_id;
            CharaGrade = chara_info.chara_grade;
            Speed = chara_info.speed;
            Stamina = chara_info.stamina;
            Power = chara_info.power;
            Wiz = chara_info.wiz;
            Guts = chara_info.guts;
            Vital = chara_info.vital;
            MaxSpeed = chara_info.max_speed;
            MaxStamina = chara_info.max_stamina;
            MaxPower = chara_info.max_power;
            MaxWiz = chara_info.max_wiz;
            MaxGuts = chara_info.max_guts;
            MaxVital = chara_info.max_vital;
            Motivation = chara_info.motivation;
            Fans = chara_info.fans;
            Rarity = chara_info.rarity;
            RaceRunningStyle = (StyleType)chara_info.race_running_style;
            GroundPropers = new Dictionary<GroundType, int>()
            {
                { GroundType.Turf, chara_info.proper_ground_turf },
                { GroundType.Dirt, chara_info.proper_ground_dirt }
            }.ToFrozenDictionary();
            StylePropers = new Dictionary<StyleType, int>()
            {
                { StyleType.Nige, chara_info.proper_running_style_nige },
                { StyleType.Senko, chara_info.proper_running_style_senko },
                { StyleType.Sashi, chara_info.proper_running_style_sashi },
                { StyleType.Oikomi, chara_info.proper_running_style_oikomi }
            }.ToFrozenDictionary();
            DistancePropers = new Dictionary<DistanceType, int>()
            {
                { DistanceType.Short, chara_info.proper_distance_short },
                { DistanceType.Mile, chara_info.proper_distance_mile },
                { DistanceType.Middle, chara_info.proper_distance_middle },
                { DistanceType.Long, chara_info.proper_distance_long }
            }.ToFrozenDictionary();
            TalentLevel = chara_info.talent_level;
            SkillArray = [.. chara_info.skill_array.Select(x => (x.skill_id, x.level))];
            DisableSkillIdArray = [.. chara_info.disable_skill_id_array];
            SkillTipsArray = [.. chara_info.skill_tips_array.Select(x => (x.group_id, x.rarity, x.level))];
            SupportCardArray = [.. chara_info.support_card_array.Select(x => (x.position, x.support_card_id, x.limit_break_count, x.exp, x.owner_viewer_id))];
            SuccessionTrainedCharaId1 = chara_info.succession_trained_chara_id_1;
            SuccessionTrainedCharaId2 = chara_info.succession_trained_chara_id_2;
            Turn = chara_info.turn;
            SkillPoint = chara_info.skill_point;
            State = chara_info.state;
            PlayingState = chara_info.playing_state;
            Scenario = (ScenarioType)chara_info.scenario_id;
            StartTime = DateTimeOffset.Parse(chara_info.start_time);
            EvaluationInfoArray = [.. chara_info.evaluation_info_array.Select(x => new EvaluationInfo(x))];
            CharaEffectIdArray = [.. chara_info.chara_effect_id_array];
            SkillUpgradeInfoArray = [.. chara_info.skill_upgrade_info_array.Select(x => (x.condition_id, x.total_count, x.current_count))];
            #endregion
            #region home_info
            var home_info = ev.data.home_info;
            CommandInfoArray = [.. home_info.command_info_array.Select(x => new CommandInfo(x))];
            #endregion
            #region unchecked_event_array
            UncheckedEventArray = [.. ev.data.unchecked_event_array.Select(x => new UncheckedEvent(x))];
            #endregion
        }
    }
    #region chara_info
    public class EvaluationInfo
    {
        public int TargetId { get; }
        public int TrainingPartnerId { get; }
        public int Evaluation { get; }
        public bool IsOutgoing { get; }
        public int StoryStep { get; }
        public bool IsAppear { get; }
        public (int CharaId, bool IsOutgoing, int StoryStep)[] GroupOutingInfoArray { get; private set; }
        public EvaluationInfo() { }
        public EvaluationInfo(Gallop.EvaluationInfo ei)
        {
            TargetId = ei.target_id;
            TrainingPartnerId = ei.training_partner_id;
            Evaluation = ei.evaluation;
            IsOutgoing = ei.is_outing == 1;
            StoryStep = ei.story_step;
            IsAppear = ei.is_appear == 1;
            GroupOutingInfoArray = ei.group_outing_info_array.Select(x => (x.chara_id, x.is_outing == 1, x.story_step)).ToArray();
        }
        public EvaluationInfo Clone()
        {
            var clone = (EvaluationInfo)MemberwiseClone();
            clone.GroupOutingInfoArray = [.. GroupOutingInfoArray];
            return clone;
        }
    }
    #endregion
    #region home_info
    public class CommandInfo
    {
        /// <summary>
        /// 训练类型
        /// </summary>
        public int CommandType { get; private set; }
        /// <summary>
        /// 训练ID，用于区分普通/合宿/远征
        /// </summary>
        public int CommandId { get; private set; }
        /// <summary>
        /// 训练是否可用
        /// </summary>
        public bool IsEnable { get; private set; }
        /// <summary>
        /// 训练人头
        /// </summary>
        public int[] TrainingPartnerArray { get; private set; }
        /// <summary>
        /// 训练红点
        /// </summary>
        public int[] TipsEventPartnerArray { get; private set; }
        /// <summary>
        /// 训练的属性变动
        /// </summary>
        public ParamsIncDecInfo ParamsIncDecInfoArray { get; private set; }
        /// <summary>
        /// 失败率%
        /// </summary>
        public int FailureRate { get; private set; }
        /// <summary>
        /// 训练等级
        /// </summary>
        public int Level { get; private set; }

        public CommandInfo() { }
        public CommandInfo(SingleModeCommandInfo ci)
        {
            CommandType = ci.command_type;
            CommandId = ci.command_id;
            IsEnable = ci.is_enable == 1;
            TrainingPartnerArray = [.. ci.training_partner_array];
            TipsEventPartnerArray = [.. ci.tips_event_partner_array];
            ParamsIncDecInfoArray = new(ci.params_inc_dec_info_array.ToDictionary(x => x.target_type, x => x.value));
            FailureRate = ci.failure_rate;
            Level = ci.level;
        }
        public CommandInfo Clone()
        {
            var clone = (CommandInfo)MemberwiseClone();
            clone.TrainingPartnerArray = [.. TrainingPartnerArray];
            clone.TipsEventPartnerArray = [.. TipsEventPartnerArray];
            clone.ParamsIncDecInfoArray = new(ParamsIncDecInfoArray);
            return clone;
        }
    }
    #endregion
    #region unchecked_event_array
    public class UncheckedEvent(SingleModeEventInfo ei)
    {
        /// <summary>
        /// 用于区分同StoryId事件的ID
        /// </summary>
        public int EventId { get; } = ei.event_id;
        /// <summary>
        /// 事件来源
        /// </summary>
        public int CharaId { get; } = ei.chara_id;
        /// <summary>
        /// 事件ID
        /// </summary>
        public int StoryId { get; } = ei.story_id;
        /// <summary>
        /// 未知
        /// </summary>
        public int PlayingTime { get; } = ei.play_timing;
        public EventContent EventContentsInfo { get; } = new(ei.event_contents_info);
        public int? SuccessionEffectType { get; } = ei.succession_event_info?.effect_type;

        public bool IsGoldenSuccession => SuccessionEffectType != null && SuccessionEffectType == 2;

        public class EventContent(EventContentsInfo eci)
        {
            public int SupportCardId { get; } = eci.support_card_id;
            public int ShowClear { get; } = eci.show_clear;
            public int ShowClearSortId { get; } = eci.show_clear_sort_id;
            public EventContentChoice[] ChoiceArray { get; } = [.. eci.choice_array.Select(x => new EventContentChoice(x))];
            public int TipsTrainingPartnerId { get; } = eci.tips_training_partner_id;

            public class EventContentChoice(ChoiceArray ca)
            {
                /// <summary>
                /// 选择ID，标识事件是否成功
                /// </summary>
                public int SelectIndex { get; } = ca.select_index;
                public int ReceiveItemId { get; } = ca.receive_item_id;
                public int TargetRaceId { get; } = ca.target_race_id;
                public int GainSelectIdIndex { get; } = ca.gain_select_id_index;
                public int SelectIcon { get; } = ca.select_icon;
            }
        }
        #endregion
    }
}