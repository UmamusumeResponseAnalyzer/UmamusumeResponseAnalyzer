using Gallop;
using Newtonsoft.Json;
using Test.Tests;
using UmamusumeResponseAnalyzer;

namespace Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Database.Initialize();
            var bytes = File.ReadAllBytes(@"C:\Users\micro\AppData\Local\UmamusumeResponseAnalyzer\packets\22-06-09 12-06-28R.msgpack");
            var obj = JsonConvert.DeserializeObject<SingleModeCheckEventResponse>(MessagePack.MessagePackSerializer.ConvertToJson(bytes));
            if (obj == null) throw new Exception("反序列化失败");
            obj.AsParseSkillTipsResponseTest()
                .Translate()
                .PrintSkillTips()
                .Run();
        }
    }
}