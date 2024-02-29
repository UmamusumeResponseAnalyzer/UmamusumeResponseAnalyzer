using Gallop;
using Spectre.Console;
using System.Text.RegularExpressions;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseTrainingRequest(Gallop.SingleModeExecCommandRequest @event)
        {
            int turn = @event.current_turn;
            if(GameStats.currentTurn!=0 && turn != GameStats.currentTurn) return;
            int trainingId = GameGlobal.ToTrainId[@event.command_id];
            if(GameStats.stats[turn]!=null)
                GameStats.stats[turn].playerChoice=trainingId;
        }
    }
}

