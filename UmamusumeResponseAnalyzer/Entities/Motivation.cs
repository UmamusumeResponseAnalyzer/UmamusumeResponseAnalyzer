using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class Motivation(int m)
    {
        public static Motivation Best => new(5);
        public static Motivation Good => new(4);
        public static Motivation Normal => new(3);
        public static Motivation Bad => new(2);
        public static Motivation Worst => new(1);

        private readonly int motivation = m;
        private readonly string enumString = m switch
        {
            1 => "绝不调",
            2 => "不调",
            3 => "普通",
            4 => "好调",
            5 => "绝好调"
        };

        public static implicit operator int(Motivation m) => m.motivation;
        public static implicit operator string(Motivation m) => m.enumString;
        public string ToColoredString()
        {
            return motivation switch
            {
                5 => $"[green]{this}[/]",
                4 => $"[yellow]{this}[/]",
                3 => $"[red]{this}[/]",
                2 => $"[red]{this}[/]",
                1 => $"[red]{this}[/]",
            };
        }
    }
}
