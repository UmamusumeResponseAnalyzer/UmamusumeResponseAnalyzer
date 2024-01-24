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
            AnsiConsole.MarkupLine($"玩家选择了选项 [aqua]{@event.choice_number}[/]");
            EventLogger.UpdatePlayerChoice(@event);
        }
    }
}

