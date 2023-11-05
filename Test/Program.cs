using Gallop;
using Newtonsoft.Json;
using Test.Tests;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Handler;

namespace Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Config.Initialize();
            Database.Initialize();
            var bytes = File.ReadAllBytes(@"C:\Users\Lipi\AppData\Local\UmamusumeResponseAnalyzer\packets\23-11-05 23-08-07-633R.msgpack");
            var obj = JsonConvert.DeserializeObject<SingleModeCheckEventResponse>(MessagePack.MessagePackSerializer.ConvertToJson(bytes));
            if (obj == null) throw new Exception("反序列化失败");
            Handlers.ParseSkillTipsResponse(obj);
        }
    }
}