using Newtonsoft.Json;

namespace UmamusumeResponseAnalyzer.Plugin
{
    public class PluginInformation
    {
        public string Author { get; set; } = string.Empty;
        public string InternalName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Changelog { get; set; } = string.Empty;
        // 服务器原样的版本字符串(可能带前导零,如 "2026.03.04")。下载 URL 必须用它原样:
        // System.Version 会把 "2026.03.04" 归一成 "2026.3.4",拼出的下载地址与服务器不符 → 404 装不上。
        [JsonProperty("version")]
        public string RawVersion { get; set; } = "0.0.0";

        // 比较/排序用的强类型版本(从 RawVersion 解析)。setter 兼容直接赋 Version 的旧用法(如测试 Info 助手)。
        [JsonIgnore]
        public Version Version
        {
            get => System.Version.TryParse(RawVersion, out var v) ? v : new(0, 0, 0);
            set => RawVersion = value.ToString();
        }
        public string[] Dependencies { get; set; } = [];
        public string[] Targets { get; set; } = [];
        public string RepositoryUrl { get; set; } = string.Empty;
        public long LastUpdate { get; set; }

        public string Category { get; set; } = string.Empty;
        public string Homepage { get; set; } = string.Empty;
        [JsonIgnore]
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
