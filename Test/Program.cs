using Gallop;
using Newtonsoft.Json;
using Test.Tests;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Handler;

namespace Test
{
    internal class Program
    {
        static void Main()
        {
            Database.Initialize();
            var bytes = File.ReadAllBytes(@"C:\Users\Lipi\AppData\Local\UmamusumeResponseAnalyzer\packets\23-11-16 20-02-15-807R.msgpack");
            var obj = JsonConvert.DeserializeObject<SingleModeCheckEventResponse>(MessagePack.MessagePackSerializer.ConvertToJson(bytes)) ?? throw new Exception("反序列化失败");
            Handlers.ParseSkillTipsResponse(obj);
        }
    }
}