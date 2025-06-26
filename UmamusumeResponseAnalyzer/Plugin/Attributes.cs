namespace UmamusumeResponseAnalyzer.Plugin
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public class LoadInHostContextAttribute : Attribute
    {
    }
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class SharedContextWithAttribute(params string[] pluginNames) : Attribute
    {
        public string[] PluginNames { get; } = pluginNames;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class AnalyzerAttribute(bool response = true, int priority = 0) : Attribute
    {
        public int Priority { get; } = priority;
        public bool Response { get; } = response;
    }
    [AttributeUsage(AttributeTargets.Property)]
    public class PluginSettingAttribute(string description = null!) : Attribute
    {
        public string Description { get; } = description ?? string.Empty;
    }
}
