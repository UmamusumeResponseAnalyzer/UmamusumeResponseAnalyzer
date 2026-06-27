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
        public Version Version { get; set; } = new(0, 0, 0);
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
