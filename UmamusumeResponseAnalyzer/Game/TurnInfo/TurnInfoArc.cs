using Gallop;
using Spectre.Console;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer.Game.TurnInfo
{
    public class TurnInfoArc : TurnInfo
    {
        public SingleModeArcInfo ArcInfo { get; }
        public SingleModeArcRival[] ArcRivalArray { get; }
        public SingleModeRivalRaceInfo[] RivalRaceInfoArray { get; }
        public SingleModeArcSelectionInfo SelectionInfo { get; }
        public SingleModeArcCommandInfo[] CommandInfoArray { get; }
        public SingleModeArcRaceHistory[] RaceHistoryArray { get; }
        public ArcEvaluationInfo[] EvaluationInfoArray { get; }
        public NotUpArcParameterInfo NotUpArcParameterInfo { get; }

        public new int TotalTurns = 67;
        public int ApprovalRate => ArcInfo.approval_rate;
        public bool IsAbroad => (Turn >= 37 && Turn <= 43) || (Turn >= 61 && Turn <= 67);

        public TurnInfoArc(SingleModeCheckEventResponse.CommonResponse resp) : base(resp)
        {
            var arc = resp.arc_data_set;
            ArcInfo = arc.arc_info;
            ArcRivalArray = arc.arc_rival_array;
            RivalRaceInfoArray = arc.rival_race_info_array;
            SelectionInfo = arc.selection_info;
            CommandInfoArray = arc.command_info_array;
            RaceHistoryArray = arc.race_history_array;
            EvaluationInfoArray = arc.evaluation_info_array;
        }
    }
}