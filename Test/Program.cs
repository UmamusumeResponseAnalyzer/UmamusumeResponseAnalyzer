using Gallop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Handler;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Test
{
    internal class Program
    {
        static void Main()
        {
            Database.Initialize();
            var bytes = File.ReadAllBytes(@"C:\Users\Lipi\AppData\Local\UmamusumeResponseAnalyzer\packets\23-11-17 14-31-26-171R.msgpack");
            dynamic dyn = JObject.Parse(MessagePack.MessagePackSerializer.ConvertToJson(bytes)) ?? throw new Exception("反序列化失败");
            if (dyn.data.single_mode_load_common != null)
            {
                var data1 = dyn.data.single_mode_load_common;
                if (dyn.data.arc_data_set != null)
                {
                    data1.arc_data_set = dyn.data.arc_data_set;
                }
                if (dyn.data.venus_data_set != null)
                {
                    data1.venus_data_set = dyn.data.venus_data_set;
                }
                dyn.data = data1;
            }
            var obj = dyn.ToObject<SingleModeCheckEventResponse>();
            Handlers.ParseSkillTipsResponse(obj);
        }
    }
}