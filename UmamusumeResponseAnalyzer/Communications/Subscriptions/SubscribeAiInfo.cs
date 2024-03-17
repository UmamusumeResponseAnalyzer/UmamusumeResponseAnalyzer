using Gallop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.AI;

namespace UmamusumeResponseAnalyzer.Communications.Subscriptions
{
    public class SubscribeAiInfo : BaseSubscription<GameStatusSend_UAF>
    {
        public SubscribeAiInfo(string wsKey) : base(wsKey) { }
    }
}
