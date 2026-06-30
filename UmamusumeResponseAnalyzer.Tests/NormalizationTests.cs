using Newtonsoft.Json.Linq;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// <see cref="ResponseNormalizer.Normalize"/> 的确定性单测——不依赖语料，始终运行。
    /// 用最小构造的 JSON 精确覆盖每条归一化规则。
    /// </summary>
    public class NormalizationTests
    {
        // 判定一个 JToken 是否“空值”（C# null 或 JSON null），兼容 Newtonsoft 两种表示
        static bool IsNullish(JToken? t) => t is null or { Type: JTokenType.Null };

        [Fact]
        public void Normalize_UnwrapsSingleModeLoadCommon()
        {
            var obj = JObject.Parse("""
                {"data":{"single_mode_load_common":{"foo":1},"cook_data_set":{"x":2},"cook_data_set_load":{"y":3}}}
                """);

            ResponseNormalizer.Normalize(obj);

            Assert.Null(obj["data"]!["single_mode_load_common"]);           // 外层壳已剥离
            Assert.Equal(1, (int)obj["data"]!["foo"]!);                     // 内层内容上提为新的 data
            Assert.Equal(2, (int)obj["data"]!["cook_data_set"]!["x"]!);     // *_data_set 被补回内层
            Assert.Equal(3, (int)obj["data"]!["cook_data_set_load"]!["y"]!); // *_data_set_load 同样
        }

        [Fact]
        public void Normalize_NullsOutCookEmptyArrayFields()
        {
            var obj = JObject.Parse("""
                {"data":{"cook_data_set":{"dish_skill_info":[],"gain_material_info":[],"last_command_info":[],"keep":5}}}
                """);

            ResponseNormalizer.Normalize(obj);

            var cook = obj["data"]!["cook_data_set"]!;
            Assert.True(IsNullish(cook["dish_skill_info"]));
            Assert.True(IsNullish(cook["gain_material_info"]));
            Assert.True(IsNullish(cook["last_command_info"]));
            Assert.Equal(5, (int)cook["keep"]!);   // 其它字段保持不变
        }

        [Fact]
        public void Normalize_NullsOutVenusEmptyArrayFields()
        {
            // venus_data_set: race_start_info / venus_race_condition 来成空数组时应置 null
            var obj = JObject.Parse("""
                {"data":{"venus_data_set":{"race_start_info":[],"venus_race_condition":[],"keep":7}}}
                """);

            ResponseNormalizer.Normalize(obj);

            var venus = obj["data"]!["venus_data_set"]!;
            Assert.True(IsNullish(venus["race_start_info"]));
            Assert.True(IsNullish(venus["venus_race_condition"]));
            Assert.Equal(7, (int)venus["keep"]!);   // 非目标字段保持不变
        }

        [Fact]
        public void Normalize_NullsOutPioneerEmptyArrayField()
        {
            // pioneer_data_set: shima_training_info 来成空数组时应置 null
            var obj = JObject.Parse("""
                {"data":{"pioneer_data_set":{"shima_training_info":[],"keep":9}}}
                """);

            ResponseNormalizer.Normalize(obj);

            var pioneer = obj["data"]!["pioneer_data_set"]!;
            Assert.True(IsNullish(pioneer["shima_training_info"]));
            Assert.Equal(9, (int)pioneer["keep"]!);   // 非目标字段保持不变
        }

        [Fact]
        public void Normalize_NullsOutLegendNestedRaceResult()
        {
            var obj = JObject.Parse("""
                {"data":{"legend_data_set":{"cm_info":{"race_result_info":[]},"popularity_info":[]}}}
                """);

            ResponseNormalizer.Normalize(obj);

            var legend = obj["data"]!["legend_data_set"]!;
            Assert.True(IsNullish(legend["cm_info"]!["race_result_info"]));
            Assert.True(IsNullish(legend["popularity_info"]));
        }

        [Fact]
        public void Normalize_LeavesNonSingleModeUntouched()
        {
            var obj = JObject.Parse("""{"data":{"foo":1},"other":2}""");
            var before = obj.ToString();

            ResponseNormalizer.Normalize(obj);

            Assert.Equal(before, obj.ToString());
        }

        [Fact]
        public void Normalize_HandlesMissingDataGracefully()
        {
            var obj = JObject.Parse("""{"no_data":true}""");

            ResponseNormalizer.Normalize(obj);   // data 不是 JObject，应原样返回、不抛

            Assert.True((bool)obj["no_data"]!);
        }
    }
}
