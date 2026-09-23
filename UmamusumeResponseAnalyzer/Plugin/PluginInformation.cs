using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace UmamusumeResponseAnalyzer.Plugin
{
    public class PluginInformation
    {
        [System.Text.Json.Serialization.JsonRequired]
        public string Author { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired]
        public string InternalName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired]
        public string DisplayName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired]
        public string Description { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired]
        public string Changelog { get; set; } = string.Empty;
        // 保留 manifest 的原始版本字符串；Version 用于数字比较。
        [JsonProperty("Version")]
        [JsonPropertyName("Version"), System.Text.Json.Serialization.JsonRequired]
        public string RawVersion { get; set; } = string.Empty;

        // 比较/排序用的强类型版本(从 RawVersion 解析)。setter 保留直接赋 Version 的调用路径(如测试 Info 助手)。
        [Newtonsoft.Json.JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public Version Version
        {
            get => System.Version.Parse(RawVersion);
            set => RawVersion = value.ToString();
        }
        [System.Text.Json.Serialization.JsonRequired]
        public string[] Dependencies { get; set; } = [];
        [System.Text.Json.Serialization.JsonRequired]
        public string RepositoryUrl { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired]
        public long LastUpdate { get; set; }

        [System.Text.Json.Serialization.JsonRequired]
        public string Category { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired]
        public string Homepage { get; set; } = string.Empty;
    }
}
