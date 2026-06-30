using MessagePack;
using Newtonsoft.Json.Linq;
using UmamusumeResponseAnalyzer;

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

        static IReadOnlyList<string> Files(string suffix) =>
            Available
                ? [.. System.IO.Directory.GetFiles(Directory!, $"*{suffix}.msgpack").OrderBy(x => x, StringComparer.Ordinal)]
                : [];

        public static IReadOnlyList<string> ResponseFiles { get; } = Files("R");
        public static IReadOnlyList<string> RequestFiles { get; } = Files("Q");
        public static IReadOnlyList<string> AllFiles { get; } = [.. RequestFiles, .. ResponseFiles];

        // 单人模式回合响应（含 data.chara_info）。解析一次并缓存，供 Tier 2/3 复用。
        static readonly Lazy<IReadOnlyList<string>> _singleMode = new(() =>
            [.. ResponseFiles.Where(f =>
            {
                try { return LoadJObject(f).HasCharaInfo(); }
                catch { return false; }
            })]);
        public static IReadOnlyList<string> SingleModeResponseFiles => _singleMode.Value;

        /// <summary>读取 msgpack → JSON → JObject，并做与生产一致的归一化（<see cref="ResponseNormalizer.Normalize"/>）。</summary>
        public static JObject LoadJObject(string path)
        {
            var json = MessagePackSerializer.ConvertToJson(File.ReadAllBytes(path));
            var obj = JObject.Parse(json);
            ResponseNormalizer.Normalize(obj);
            return obj;
        }

        // ── MemberData 源：每行一个文件路径；语料缺失时回退到单个 [null] 占位行，让 Theory 走 Assert.Skip ──
        public static IEnumerable<object?[]> AllCases() => Wrap(AllFiles);
        public static IEnumerable<object?[]> ResponseCases() => Wrap(ResponseFiles);
        public static IEnumerable<object?[]> RequestCases() => Wrap(RequestFiles);
        public static IEnumerable<object?[]> SingleModeCases() => Wrap(SingleModeResponseFiles);

        static IEnumerable<object?[]> Wrap(IReadOnlyList<string> files) =>
            files.Count == 0 ? [[null]] : files.Select(f => new object?[] { f });
    }
}
