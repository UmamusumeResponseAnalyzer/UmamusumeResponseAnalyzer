using Gallop;
using Spectre.Console;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer.Game.TurnInfo
{
    public class TurnInfoArc : TurnInfo
    {
        public ArcInfo ArcInfo { get; }
        public ArcRival[] ArcRivalArray { get; }
        public (int ProgramId, int CharaId)[] RivalRaceInfoArray { get; }
        public ArcSelectionInfo SelectionInfo { get; }
        public new ArcCommandInfo[] CommandInfoArray { get; }
        public (int RaceNum, int Turn, int ResultRank)[] RaceHistoryArray { get; }
        public new ArcEvaluationInfo[] EvaluationInfoArray { get; }
        public (int[] RivalBoostCharaIdArray, bool AllRivalBoostFlag) NotUpArcParameterInfo { get; }

        public TurnInfoArc(SingleModeCheckEventResponse ev) : base(ev)
        {
            var arc = ev.data.arc_data_set;
            ArcInfo = new(arc.arc_info);
            ArcRivalArray = [.. arc.arc_rival_array.Select(x => new ArcRival(x))];
            RivalRaceInfoArray = [.. arc.rival_race_info_array.Select(x => (x.program_id, x.chara_id))];
            SelectionInfo = new(arc.selection_info);
            CommandInfoArray = [.. base.CommandInfoArray.Select(x => ArcCommandInfo.From(x, arc.command_info_array.First(y => y.command_id == x.CommandId).add_global_exp))];
            RaceHistoryArray = [.. arc.race_history_array.Select(x => (x.race_num, x.turn, x.result_rank))];
            EvaluationInfoArray = [.. base.EvaluationInfoArray.Select(x => ArcEvaluationInfo.From(x, arc.evaluation_info_array.FirstOrDefault(y => y.target_id == x.TargetId)?.chara_id))];
        }
    }
    public class ArcInfo(SingleModeArcInfo ai)
    {
        public int ApprovalRate { get; } = ai.approval_rate;
        public int GlobalExp { get; } = ai.global_exp;
        public Potential[] PotentialArray { get; } = [.. ai.potential_array.Select(x => new Potential(x))];
        public int SpTagBoostType { get; } = ai.sp_tag_boost_type;
        public int SSMatchWinCount { get; } = ai.ss_match_win_count;
        public int SpecialSSMatchWinCount { get; } = ai.special_ss_match_win_count;

        public class Potential(ArcPotential ap)
        {
            public int PotentialId { get; } = ap.potential_id;
            public int Level { get; } = ap.level;
            public (int ConditionId, int TotalCount, int CurrentCount)[] ProgressArray { get; } = [.. ap.progress_array.Select(x => (x.condition_id, x.total_count, x.current_count))];
        }
    }
    public class ArcRival(SingleModeArcRival ar)
    {
        public int CharaId { get; } = ar.chara_id;
        public int ScoutCharaId { get; } = ar.scout_chara_id;
        public int ApprovalPoint { get; } = ar.approval_point;
        public int Speed { get; } = ar.speed;
        public int Stamina { get; } = ar.stamina;
        public int Power { get; } = ar.power;
        public int Guts { get; } = ar.guts;
        public int Wiz { get; } = ar.wiz;
        public int RivalBoost { get; } = ar.rival_boost;
        public int StarLv { get; } = ar.star_lv;
        public int CommandId { get; } = ar.command_id;
        public (int PotentialId, int Level)[] PotentialArray { get; } = [.. ar.potential_array.Select(x => (x.potential_id, x.level))];
        public (int EffectNum, int EffectGroupId, int EffectValue)[] SelectionPeffArray { get; } = [.. ar.selection_peff_array.Select(x => (x.effect_num, x.effect_group_id, x.effect_value))];
        public int Rank { get; } = ar.rank;
    }
    public class ArcSelectionInfo(SingleModeArcSelectionInfo si)
    {
        public int AllWinApprovalPoint { get; } = si.all_win_approval_point;
        public ParamsIncDecInfo ParamsIncDecInfoArray { get; } = new(si.params_inc_dec_info_array.ToDictionary(x => x.target_type, x => x.value));
        public (int CharaId, int Mark, int WinApprovalPoint, int loseApprovalPoint, int RivalWinApprovalPoint, int RivalLoseApprovalPoint)[] SelectionRivalInfoArray { get; } = [.. si.selection_rival_info_array.Select(x => (x.chara_id, x.mark, x.win_approval_point, x.lose_approval_point, x.rival_win_approval_point, x.rival_lose_approval_point))];
        public ParamsIncDecInfo BonusParamsIncDecInfoArray { get; } = new(si.bonus_params_inc_dec_info_array.ToDictionary(x => x.target_type, x => x.value));
        public bool IsSpecialMatch { get; } = si.is_special_match == 1;
    }
    public class ArcCommandInfo : CommandInfo
    {
        public int AddGlobalExp { get; private set; }
        public static ArcCommandInfo From(CommandInfo ci, int age)
        {
            var aci = (ArcCommandInfo)ci.Clone();
            aci.AddGlobalExp = age;
            return aci;
        }
    }
    public class ArcEvaluationInfo : EvaluationInfo
    {
        public int CharaId { get; private set; }
        public static ArcEvaluationInfo From(EvaluationInfo ei, int? ci)
        {
            var aei = (ArcEvaluationInfo)ei.Clone();
            aei.CharaId = ci ?? 0;
            return aei;
        }
    }
}