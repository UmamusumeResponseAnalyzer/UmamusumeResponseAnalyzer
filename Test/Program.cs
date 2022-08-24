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
            var bytes = File.ReadAllBytes(@"C:\Users\micro\AppData\Local\UmamusumeResponseAnalyzer\packets\22-08-24 11-19-41-293R.msgpack");
            var obj = JsonConvert.DeserializeObject<SingleModeCheckEventResponse>(MessagePack.MessagePackSerializer.ConvertToJson(bytes));
            if (obj == null) throw new Exception("反序列化失败");
            obj.AsParseCommandInfoResponseTest()
                .Run();
        }
    }
}