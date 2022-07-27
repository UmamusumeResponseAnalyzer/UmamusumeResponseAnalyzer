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
            var bytes = File.ReadAllBytes(@"C:\Users\micro\AppData\Local\UmamusumeResponseAnalyzer\packets\22-07-26 20-52-52-663R.msgpack");
            var obj = JsonConvert.DeserializeObject<FriendSearchResponse>(MessagePack.MessagePackSerializer.ConvertToJson(bytes));
            if (obj == null) throw new Exception("反序列化失败");
            //obj.AsParseSingleModeCheckEventResponseTest()
                //.Run();
            Console.WriteLine(JsonConvert.SerializeObject(obj, Formatting.Indented));
            File.WriteAllText(@"C:\Users\micro\AppData\Local\UmamusumeResponseAnalyzer\packets\22-07-26 20-52-52-663R.json", JsonConvert.SerializeObject(obj, Formatting.Indented));
        }
    }
}