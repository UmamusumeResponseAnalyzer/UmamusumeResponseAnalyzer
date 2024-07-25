using Gallop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.AI;

namespace UmamusumeResponseAnalyzer.Communications.Subscriptions
{
    // 临时解决方案，传入连接全改成string handler
    public class SubscribeAiInfo : BaseSubscription<string>
    {
        public SubscribeAiInfo(string wsKey) : base(wsKey) { }
    }
}
