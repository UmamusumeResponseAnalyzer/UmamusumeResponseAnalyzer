using MessagePack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Handler
{
    public static partial class Handlers
    {
        private static T? TryDeserialize<T>(byte[] buffer)
        {
            try
            {
                return MessagePackSerializer.Deserialize<T>(buffer);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                var json = MessagePackSerializer.ConvertToJson(buffer);
                return JsonConvert.DeserializeObject<T>(json);
            }
        }
    }
}
