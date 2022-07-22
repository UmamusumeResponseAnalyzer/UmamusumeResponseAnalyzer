using Gallop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Handler;

namespace Test.Tests
{
    public class ParseSingleModeCheckEventResponseTest
    {
        public SingleModeCheckEventResponse @event;
        public ParseSingleModeCheckEventResponseTest(SingleModeCheckEventResponse @event)
        {
            this.@event = @event;
        }
        public void Run()
        {
            Handlers.ParseSingleModeCheckEventResponse(@event);
        }
    }
    public static class ParseSingleModeCheckEventResponseExtension
    {
        public static ParseSingleModeCheckEventResponseTest AsParseSingleModeCheckEventResponseTest(this SingleModeCheckEventResponse obj)
        {
            return new ParseSingleModeCheckEventResponseTest(obj);
        }
    }
}
