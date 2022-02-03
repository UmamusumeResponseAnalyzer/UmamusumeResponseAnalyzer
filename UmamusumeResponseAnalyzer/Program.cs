
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace UmamusumeResponseAnalyzer
{
    public static class UmamusumeResponseAnalyzer
    {
        public static void Main()
        {
#if DEBUG
            var msgpackPath = @"response/637795073363227948.bin";
            var msgpackBytes = File.ReadAllBytes(msgpackPath);
            var jsonPath = @"response/637795073363227948.json";
            var json = MessagePack.MessagePackSerializer.ConvertToJson(msgpackBytes);
            File.WriteAllText(jsonPath, JObject.Parse(json).ToString());
            File.WriteAllText(jsonPath + ".msgpack.json", JsonConvert.SerializeObject(Server.TryDeserialize<Gallop.SingleModeCheckEventResponse>(msgpackBytes), Formatting.Indented));
#endif
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