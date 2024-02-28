using Gallop;
using Spectre.Console;
using System.Text.RegularExpressions;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        public static void ParseChoiceRequest(Gallop.SingleModeChoiceRequest @event)
        {
            EventLogger.UpdatePlayerChoice(@event);
        }
    }
}

