using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.LocalizedLayouts.Handlers
{
    public static class ParseTeamStadiumOpponentListResponse
    {
        public static int MinimumConsoleWidth => Thread.CurrentThread.CurrentUICulture.Name switch
        {
            "zh-CN" => 160,
            "ja-JP" => 220,
            "en-US" => 200
        };
        public static string ColumnWidth = Thread.CurrentThread.CurrentUICulture.Name switch
        {
            "zh-CN" => "　　　",
            "ja-JP" => "　　　　　",
            "en-US" => "　　　　"
        };
    }
}
