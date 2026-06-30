using Gallop;
using Gallop.Endpoints;
using MessagePack;
using Newtonsoft.Json.Linq;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// 定位并加载真实抓包语料（一整局日服游戏，312 对 Q/R）。
    /// 语料含账号隐私（viewer_id 等）故不入 repo：默认读
    /// <c>%LocalAppData%\UmamusumeResponseAnalyzer\full_game_packets</c>，可用环境变量
    /// <c>URA_PACKET_CORPUS</c> 覆盖。语料不存在时各 Theory 以单个 [null] 占位行 + Assert.Skip
    /// 表现为“跳过”而非“失败”，保证换机 / CI 上仍可编译运行。
    /// </summary>
    public static class PacketCorpus
    {
        public sealed record EndpointPacket(string Path, GameEndpointDescriptor Endpoint);
        public sealed record UnresolvedEndpointPacket(string Path, string CanonicalUrl, Exception Error);

        public static string? Directory { get; } = Resolve();
        public static bool Available => Directory is not null;

        static string? Resolve()
        {
            var env = Environment.GetEnvironmentVariable("URA_PACKET_CORPUS");
            if (!string.IsNullOrWhiteSpace(env) && System.IO.Directory.Exists(env)) return env;
            var def = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UmamusumeResponseAnalyzer", "full_game_packets");
            return System.IO.Directory.Exists(def) ? def : null;
        }

        static IReadOnlyList<string> Files(AnalyzerKind kind) =>
            Available
                ? [.. System.IO.Directory.GetFiles(Directory!, "*.msgpack")
                    .Where(x => TryGetPacketKind(x, out var actual) && actual == kind)
                    .OrderBy(x => x, StringComparer.Ordinal)]
                : [];

        public static IReadOnlyList<string> ResponseFiles { get; } = Files(AnalyzerKind.Response);
        public static IReadOnlyList<string> RequestFiles { get; } = Files(AnalyzerKind.Request);
        public static IReadOnlyList<string> AllFiles { get; } = [.. RequestFiles, .. ResponseFiles];

        static readonly Lazy<IReadOnlyList<EndpointPacket>> _responseEndpointPackets = new(() =>
            [.. ResponseFiles.Select(TryCreateResponseEndpointPacket).OfType<EndpointPacket>()]);
        public static IReadOnlyList<EndpointPacket> ResponseEndpointPackets => _responseEndpointPackets.Value;

        static readonly Lazy<IReadOnlyList<UnresolvedEndpointPacket>> _unresolvedResponseEndpointPackets = new(() =>
            [.. ResponseFiles.Select(TryCreateUnresolvedResponseEndpointPacket).OfType<UnresolvedEndpointPacket>()]);
        public static IReadOnlyList<UnresolvedEndpointPacket> UnresolvedResponseEndpointPackets => _unresolvedResponseEndpointPackets.Value;

        // 领域层只消费 SingleModeCheckEventResponse；其它单人模式 DTO 由 catalog descriptor 测试覆盖。
        static readonly Lazy<IReadOnlyList<string>> _singleMode = new(() =>
            [.. ResponseEndpointPackets
                .Where(x => x.Endpoint.ResponseType == typeof(SingleModeCheckEventResponse))
                .Select(x => x.Path)]);
        public static IReadOnlyList<string> SingleModeResponseFiles => _singleMode.Value;

        static readonly Lazy<IReadOnlyList<string>> _singleModeTurns = new(() =>
            [.. SingleModeResponseFiles.Where(f =>
            {
                try { return HasTurnInfo(LoadJObject(f)); }
                catch { return false; }
            })]);
        public static IReadOnlyList<string> SingleModeTurnResponseFiles => _singleModeTurns.Value;

        /// <summary>读取原始 msgpack bytes。DTO materialization 测试必须走这条路径。</summary>
        public static byte[] LoadBytes(string path) => File.ReadAllBytes(path);

        /// <summary>读取 msgpack → JSON → JObject，仅用于语料筛选 / inspection。</summary>
        public static JObject LoadJObject(string path)
        {
            var json = MessagePackSerializer.ConvertToJson(LoadBytes(path));
            return JObject.Parse(json);
        }

        // ── MemberData 源：每行一个文件路径；语料缺失时回退到单个 [null] 占位行，让 Theory 走 Assert.Skip ──
        public static IEnumerable<object?[]> AllCases() => Wrap(AllFiles);
        public static IEnumerable<object?[]> ResponseCases() => Wrap(ResponseFiles);
        public static IEnumerable<object?[]> RequestCases() => Wrap(RequestFiles);
        public static IEnumerable<object?[]> SingleModeCases() => Wrap(SingleModeResponseFiles);
        public static IEnumerable<object?[]> SingleModeTurnCases() => Wrap(SingleModeTurnResponseFiles);
        public static IEnumerable<object?[]> ResponseDescriptorCases()
            => ResponseEndpointPackets.Count == 0
                ? [[null, null]]
                : ResponseEndpointPackets.Select(x => new object?[] { x.Path, x.Endpoint.ResponseType });

        static IEnumerable<object?[]> Wrap(IReadOnlyList<string> files) =>
            files.Count == 0 ? [[null]] : files.Select(f => new object?[] { f });

        static bool HasTurnInfo(JObject jo) =>
            jo["data"] is JObject data &&
            data.ContainsKey("chara_info") &&
            data["home_info"] is JObject &&
            data["unchecked_event_array"] is JArray &&
            data["race_condition_array"] is JArray &&
            data["race_start_info"] is JObject;

        static EndpointPacket? TryCreateResponseEndpointPacket(string path)
        {
            if (!TryGetCanonicalUrl(path, out var kind, out var canonicalUrl) || kind != AnalyzerKind.Response)
                return null;

            try
            {
                return new(path, Server.ResolveEndpoint(canonicalUrl));
            }
            catch
            {
                return null;
            }
        }

        static UnresolvedEndpointPacket? TryCreateUnresolvedResponseEndpointPacket(string path)
        {
            if (!TryGetCanonicalUrl(path, out var kind, out var canonicalUrl) || kind != AnalyzerKind.Response)
                return null;

            try
            {
                Server.ResolveEndpoint(canonicalUrl);
                return null;
            }
            catch (Exception ex)
            {
                return new(path, canonicalUrl, ex);
            }
        }

        static bool TryGetPacketKind(string path, out AnalyzerKind kind)
        {
            if (TryGetCanonicalUrl(path, out kind, out _))
                return true;

            var stem = Path.GetFileNameWithoutExtension(path);
            if (stem.EndsWith("Q", StringComparison.Ordinal))
            {
                kind = AnalyzerKind.Request;
                return true;
            }

            if (stem.EndsWith("R", StringComparison.Ordinal))
            {
                kind = AnalyzerKind.Response;
                return true;
            }

            return false;
        }

        internal static bool TryGetCanonicalUrl(string path, out AnalyzerKind kind, out string canonicalUrl)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            var requestMarker = stem.IndexOf("Q-", StringComparison.Ordinal);
            var responseMarker = stem.IndexOf("R-", StringComparison.Ordinal);

            if (requestMarker >= 0 && (responseMarker < 0 || requestMarker < responseMarker))
            {
                kind = AnalyzerKind.Request;
                canonicalUrl = Uri.UnescapeDataString(stem[(requestMarker + 2)..]);
                return canonicalUrl.Length > 0;
            }

            if (responseMarker >= 0)
            {
                kind = AnalyzerKind.Response;
                canonicalUrl = Uri.UnescapeDataString(stem[(responseMarker + 2)..]);
                return canonicalUrl.Length > 0;
            }

            canonicalUrl = string.Empty;
            kind = default;
            return false;
        }
    }
}
