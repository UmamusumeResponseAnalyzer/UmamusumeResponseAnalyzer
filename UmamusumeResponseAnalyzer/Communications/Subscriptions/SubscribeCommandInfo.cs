using Gallop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Communications.Subscriptions
{
    public class SubscribeCommandInfo : BaseSubscription<SingleModeCheckEventResponse>
    {
        public SubscribeCommandInfo(string wsKey) : base(wsKey) { }
    }
}
