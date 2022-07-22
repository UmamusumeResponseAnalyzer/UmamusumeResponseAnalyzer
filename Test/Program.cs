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
            //var bytes = File.ReadAllBytes(@"C:\Users\micro\AppData\Local\UmamusumeResponseAnalyzer\packets\22-07-21 09-09-40-661R.msgpack");
            //var bytes = File.ReadAllBytes(@"C:\Users\micro\AppData\Local\UmamusumeResponseAnalyzer\packets\22-07-21 09-42-34-126R.msgpack");
            var bytes = File.ReadAllBytes(@"C:\Users\micro\AppData\Local\UmamusumeResponseAnalyzer\packets\22-07-21 09-49-49-534R.msgpack");
            var obj = JsonConvert.DeserializeObject<SingleModeCheckEventResponse>(MessagePack.MessagePackSerializer.ConvertToJson(bytes));
            if (obj == null) throw new Exception("反序列化失败");
            obj.AsParseSingleModeCheckEventResponseTest()
                .Run();
            Console.WriteLine(JsonConvert.SerializeObject(obj, Formatting.Indented));
        }
    }
}