using Gallop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Handler;

namespace Test.Tests
{
    public class ParseCommandInfoResponseTest
    {
        public SingleModeCheckEventResponse @event;
        public ParseCommandInfoResponseTest(SingleModeCheckEventResponse @event)
        {
            this.@event = @event;
        }
        public void Run()
        {
            Handlers.ParseCommandInfo(@event);
        }
    }
    public static class ParseCommandInfoResponseTestExtension
    {
        public static ParseCommandInfoResponseTest AsParseCommandInfoResponseTest(this SingleModeCheckEventResponse obj)
        {
            return new ParseCommandInfoResponseTest(obj);
        }
    }
}
