using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Communications
{
    public class WSRequest
    {
        public CommandType CommandType { get; set; }
        public string Command { get; set; }
        public string[] Parameters { get; set; }

        public WSRequest(CommandType type, string cmd, string[] prms)
        {
            CommandType = type;
            Command = cmd;
            Parameters = prms;
        }
    }
}
