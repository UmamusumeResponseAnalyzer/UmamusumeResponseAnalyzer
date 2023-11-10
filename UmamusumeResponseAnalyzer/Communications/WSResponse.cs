using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Communications
{
    public class WSResponse
    {
        public WSResponseResultCode Result { get; set; }
        public string Reason { get; set; }

        public enum WSResponseResultCode
        {
            Fail,
            Success
        }
    }
}
