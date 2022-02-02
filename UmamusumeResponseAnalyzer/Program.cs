
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace UmamusumeResponseAnalyzer
{
    public static class UmamusumeResponseAnalyzer
    {
        public static void Main()
        {
            //MessagePack.MessagePackSerializer.Deserialize<Gallop.TrainedCharaLoadResponse>(File.ReadAllBytes("637792390882286601.msgpack"));
            //File.WriteAllText("637792390882286601.json", JObject.Parse(MessagePack.MessagePackSerializer.ConvertToJson(File.ReadAllBytes("637792390882286601.msgpack"))).ToString());
            Database.Initialize();
            Server.Start();
            Console.WriteLine("已启动.");
            while (true)
            {
                Console.ReadLine();
            }
        }
    }
}