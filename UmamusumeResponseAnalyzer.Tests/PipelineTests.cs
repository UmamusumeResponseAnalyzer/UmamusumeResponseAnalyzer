using MessagePack;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>Tier 1：MessagePack → JSON → JObject 管线对全部真实抓包不抛异常。最基础的回归网。</summary>
    public class PipelineTests
    {
        [Fact]
        public void Corpus_IsPresentAndPaired()
        {
            Assert.SkipUnless(PacketCorpus.Available,
                "未找到抓包语料：设置环境变量 URA_PACKET_CORPUS，或放到 %LocalAppData%\\UmamusumeResponseAnalyzer\\full_game_packets");
            Assert.NotEmpty(PacketCorpus.RequestFiles);
            Assert.NotEmpty(PacketCorpus.ResponseFiles);
            // 每个游戏请求(Q)都应有一个对应响应(R)
            Assert.Equal(PacketCorpus.RequestFiles.Count, PacketCorpus.ResponseFiles.Count);
        }

        [Fact]
        public void PacketCorpus_ParsesCurrentDebugPacketFileName()
        {
            var canonicalUrl = "https://example.test/account/index?viewer_id=1#fragment";
            var path = Path.Combine(
                Path.GetTempPath(),
                $"26-06-30 12-00-00-000R-{Uri.EscapeDataString(canonicalUrl)}.msgpack");

            var parsed = PacketCorpus.TryGetCanonicalUrl(path, out var kind, out var url);

            Assert.True(parsed);
            Assert.Equal(AnalyzerKind.Response, kind);
            Assert.Equal(canonicalUrl, url);
        }

        [Theory]
        [MemberData(nameof(PacketCorpus.AllCases), MemberType = typeof(PacketCorpus))]
        public void EveryPacket_ConvertsToJson(string? path)
        {
            Assert.SkipWhen(path is null, "无语料");
            var bytes = File.ReadAllBytes(path!);
            var json = MessagePackSerializer.ConvertToJson(bytes);
            Assert.False(string.IsNullOrWhiteSpace(json));
        }

        [Theory]
        [MemberData(nameof(PacketCorpus.ResponseCases), MemberType = typeof(PacketCorpus))]
        public void EveryResponse_ParsesToJsonObject(string? path)
        {
            Assert.SkipWhen(path is null, "无语料");
            var obj = PacketCorpus.LoadJObject(path!);
            Assert.NotNull(obj);
        }
    }
}
