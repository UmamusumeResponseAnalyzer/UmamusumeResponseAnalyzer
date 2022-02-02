
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
            var msgpackPath = @"response/637794164192625183.msgpack";
            var msgpackBytes = File.ReadAllBytes(msgpackPath).Replace(new byte[] { 0x88,0xC0,0x01},new byte[] {0x87 });
            var jsonPath = @"response/637794164192625183.json";
            var json = MessagePack.MessagePackSerializer.ConvertToJson(msgpackBytes);
            File.WriteAllText(jsonPath, JObject.Parse(json).ToString());
            File.WriteAllText(jsonPath + "msgpack.json", JsonConvert.SerializeObject(MessagePack.MessagePackSerializer.Deserialize<Gallop.TeamStadiumOpponentListResponse>(msgpackBytes), Formatting.Indented));
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