using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Plugin
{
    public class PluginInformation
    {
        public string Author { get; set; }
        public string InternalName { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Changelog { get; set; }
        public Version Version { get; set; }
        public string[] Dependencies { get; set; }
        public string[] Targets { get; set; }
        public string RepositoryUrl { get; set; }
        public string DownloadUrl { get; set; }
        public long LastUpdate { get; set; }
    }
}
