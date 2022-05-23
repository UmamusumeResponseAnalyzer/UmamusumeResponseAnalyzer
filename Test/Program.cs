using Gallop;
using Newtonsoft.Json;
using UmamusumeResponseAnalyzer;

namespace Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Database.Initialize();
            var checkEventResponse = JsonConvert.DeserializeObject<SingleModeCheckEventResponse>(File.ReadAllText(@"C:\Users\micro\source\repos\UmamusumeResponseAnalyzer\UmamusumeResponseAnalyzer\bin\Debug\net6.0\packets\22-05-23 23-29-28R.json"));
            if (checkEventResponse == null) throw new Exception("反序列化失败");
            checkEventResponse
                .WithStoryId(830041001)
                .AddChoices(2,1)
                .Run();
        }
    }
}