using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.LocalizedLayout.Handlers
{
    public class CommandInfoLayout
    {
        public static CommandInfoLayout Current
        {
            get
            {
                var culture = Thread.CurrentThread.CurrentUICulture.Name switch
                {
                    "zh-CN" => "SimplifiedChinese",
                    "en-US" => "English",
                    "ja-JP" => "Japanese"
                };
                var layout = typeof(CommandInfoLayout).GetField(culture, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return (CommandInfoLayout)layout!.GetValue(null)!;
            }
        }
        public static CommandInfoLayout SimplifiedChinese = new(16);
        public static CommandInfoLayout Japanese = new(17);
        public static CommandInfoLayout English = new(15);
        public int TrainingCardWidth { get; init; }
        public int MainSectionWidth => TrainingCardWidth * 5 + 10;
        public CommandInfoLayout(int tcw)
        {
            TrainingCardWidth = tcw;
        }
    }
}
