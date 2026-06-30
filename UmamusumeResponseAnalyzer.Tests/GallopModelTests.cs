using Gallop;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// Tier 2：真实单人模式响应能反序列化进 Gallop 强类型模型（与插件一致走 MessagePack）。
    /// </summary>
    public class GallopModelTests
    {
        [Fact]
        public void ResponseCorpus_HasNoUnresolvedEndpoints()
        {
            var unresolved = PacketCorpus.UnresolvedResponseEndpointPackets;

            Assert.Empty(unresolved);
        }

        [Theory]
        [MemberData(nameof(PacketCorpus.ResponseDescriptorCases), MemberType = typeof(PacketCorpus))]
        public void Response_DeserializesToCatalogModel(string? path, Type? responseType)
        {
            Assert.SkipWhen(path is null || responseType is null, "无带 canonical URL 的响应语料");

            var dto = MessagePackSerializer.Deserialize(responseType!, PacketCorpus.LoadBytes(path!));

            Assert.NotNull(dto);
        }

        [Theory]
        [MemberData(nameof(PacketCorpus.SingleModeCases), MemberType = typeof(PacketCorpus))]
        public void SingleModeCheckEventResponse_DeserializesToModel(string? path)
        {
            Assert.SkipWhen(path is null, "无 SingleModeCheckEventResponse 语料");

            var resp = MessagePackSerializer.Deserialize<SingleModeCheckEventResponse>(PacketCorpus.LoadBytes(path!));

            Assert.NotNull(resp);
            Assert.NotNull(resp!.data);
            Assert.NotNull(resp.data.chara_info);
            // card_id 被成功映射(非 0)即证明 string-key → 字段名 的反序列化链路正确
            Assert.True(resp.data.chara_info.card_id > 0, "card_id 应被正确反序列化");
        }

        [Fact]
        public void MapNilValue_LeavesValueTypeFieldDefault()
        {
            var payload = MessagePackSerializer.Serialize(
                new Dictionary<string, object?> { ["special_home_id"] = null },
                ContractlessStandardResolver.Options);

            var dto = MessagePackSerializer.Deserialize<SingleModeHomeInfo>(payload);

            Assert.NotNull(dto);
            Assert.Equal(0, dto!.special_home_id);
        }

        [Fact]
        public void RootNil_DeserializesReferenceAsNull()
        {
            var dto = MessagePackSerializer.Deserialize<SingleModeHomeInfo>(new byte[] { MessagePackCode.Nil });

            Assert.Null(dto);
        }
    }
}
