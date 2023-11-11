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
            Database.Initialize();
            var bytes = File.ReadAllBytes(@"C:\Users\Lipi\AppData\Local\UmamusumeResponseAnalyzer\packets\23-11-11 13-56-01-015R.msgpack");
            var obj = JsonConvert.DeserializeObject<SingleModeCheckEventResponse>(MessagePack.MessagePackSerializer.ConvertToJson(bytes)) ?? throw new Exception("反序列化失败");
            obj.data.chara_info.skill_array = obj.data.chara_info.skill_array.Where(x => x.skill_id != 202402).ToArray();
            obj.data.chara_info.skill_point += 4000;
            Handlers.ParseSkillTipsResponse(obj);
        }
    }
}