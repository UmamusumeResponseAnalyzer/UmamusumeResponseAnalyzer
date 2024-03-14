using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Communications.Actions
{
    public class PrintText : ICommand
    {
        public CommandType CommandType => CommandType.Action;
        string Text { get; init; }
        public PrintText(string text) => Text = text;
        public WSResponse? Execute()
        {
            AnsiConsole.MarkupLine(Text);
            return null;
        }
    }
}
