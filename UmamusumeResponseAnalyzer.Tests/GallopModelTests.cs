using Gallop;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// Tier 2：真实单人模式响应能反序列化进 Gallop 强类型模型（与插件一致走 Newtonsoft
    /// <c>JObject.ToObject&lt;SingleModeCheckEventResponse&gt;()</c>）。验证 ~128 个 DTO 与真实协议对齐。
    /// </summary>
    public class GallopModelTests
    {
        [Theory]
        [MemberData(nameof(PacketCorpus.SingleModeCases), MemberType = typeof(PacketCorpus))]
        public void SingleModeResponse_DeserializesToModel(string? path)
        {
            Assert.SkipWhen(path is null, "无单人模式语料");

            var obj = PacketCorpus.LoadJObject(path!);
            var resp = obj.ToObject<SingleModeCheckEventResponse>();

            Assert.NotNull(resp);
            Assert.NotNull(resp!.data);
            Assert.NotNull(resp.data.chara_info);
            // card_id 被成功映射(非 0)即证明 string-key → 字段名 的反序列化链路正确
            Assert.True(resp.data.chara_info.card_id > 0, "card_id 应被正确反序列化");
        }
    }
}
