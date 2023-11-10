using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Communications
{
    public interface ICommand
    {
        public CommandType CommandType { get; }
        public WSResponse? Execute();
    }
}
