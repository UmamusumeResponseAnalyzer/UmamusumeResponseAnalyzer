using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Plugin
{
    internal static class PluginManager
    {
        internal static List<string> PLUGIN_FILES => [.. new DirectoryInfo("Plugins").GetFiles("*.dll", SearchOption.AllDirectories).Select(x => x.FullName)];
        /// <summary>
        /// Key是插件名称
        /// </summary>
        private static Dictionary<string, PluginMetadata> Metadatas { get; } = [];
        internal static List<string> FailedPlugins { get; } = [];
        internal static List<IPlugin> LoadedPlugins { get; } = [];
        internal static SortedDictionary<int, List<(IPlugin Self, MethodInfo Method)>> RequsetAnalyzerMethods { get; } = [];
        internal static SortedDictionary<int, List<(IPlugin Self, MethodInfo Method)>> ResponseAnalyzerMethods { get; } = [];
        internal static List<HashSet<string>> ContextGroups { get; } = [];
        internal static Dictionary<string, PluginLoadContext> Contexts { get; } = [];
        internal static List<Assembly> Assemblies { get; } = [];

        internal static void Init()
        {
            Directory.CreateDirectory("Plugins");
            foreach (var dll in PLUGIN_FILES)
            {
                if (dll.EndsWith(".resources.dll") && !dll.Contains(LanguageConfig.GetCulture())) continue;
                var metadata = LoadMetadata(dll);
                Metadatas.Add(metadata.PluginName, metadata);
            }

            foreach (var metadata in Metadatas.Values.Where(x => x.SharedContextsWith.Count != 0))
            {
                metadata.SharedContextsWith.RemoveAll(x => Metadatas[x].LoadInHost);
                foreach (var share in metadata.SharedContextsWith)
                {
                    var existingGroup = ContextGroups.FirstOrDefault(x => x.Contains(share));
                    // 这个插件已经和别的插件一个组了，加入自己
                    if (existingGroup != default)
                    {
                        existingGroup.Add(metadata.PluginName);
                    }
                    else
                    {
                        ContextGroups.Add([share, metadata.PluginName]);
                    }
                }
            }
            foreach (var metadata in Metadatas.Values.Where(x => !x.LoadInHost && x.SharedContextsWith.Count == 0))
            {
                var existingGroup = ContextGroups.FirstOrDefault(x => x.Contains(metadata.PluginName));
                if (existingGroup == default)
                {
                    ContextGroups.Add([metadata.PluginName]);
                }
            }

            foreach (var loadInHostPlugin in Metadatas.Where(x => x.Value.LoadInHost))
            {
                var assembly = AssemblyLoadContext.Default.LoadFromStream(PluginLoadContext.LoadStreamIndependent(loadInHostPlugin.Value.FilePath));
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    var localPath = Metadatas.FirstOrDefault(x => x.Value.FilePath.EndsWith($"{reference.Name}.dll")).Value?.FilePath;
                    if (localPath == default || AssemblyLoadContext.Default.Assemblies.Any(x => x.GetName().Name == reference.Name)) continue;
                    AssemblyLoadContext.Default.LoadFromStream(PluginLoadContext.LoadStreamIndependent(localPath));
                }
                var type = assembly.GetExportedTypes().FirstOrDefault(x => typeof(IPlugin).IsAssignableFrom(x)) ?? throw new TypeLoadException();
                if (Activator.CreateInstance(type) is IPlugin plugin)
                {
                    RegisterHandlers(plugin);
                    Assemblies.Add(assembly);
                }
                else
                {
                    FailedPlugins.Add(loadInHostPlugin.Value.FilePath);
                }
                Assemblies.Add(assembly);
            }
            foreach (var group in ContextGroups)
            {
                LoadContextGroup(group);
            }
        }
        internal static PluginMetadata LoadMetadata(string pluginPath)
        {
            var tempContext = new PluginLoadContext("LoadMetadataContext");
            var tempAssembly = tempContext.LoadFromAssemblyPath(pluginPath);
            var shouldLoadInHost = tempAssembly.GetCustomAttribute<LoadInHostContextAttribute>() != null;
            var sharedContextsWith = tempAssembly.GetCustomAttributes<SharedContextWithAttribute>().SelectMany(x => x.PluginNames).ToList();
            var metadata = new PluginMetadata(pluginPath, tempAssembly.GetName().Name ?? string.Empty, shouldLoadInHost, sharedContextsWith);
            tempContext.Unload();
            return metadata;
        }
        internal static void LoadContextGroup(HashSet<string> group)
        {
            var contextKey = string.Join("&", group);
            PluginLoadContext context;
            if (!Contexts.TryGetValue(contextKey, out context!))
            {
                context = new PluginLoadContext(contextKey);
                Contexts.Add(contextKey, context);
            }
            foreach (var plugin in group)
            {
                LoadPlugin(context, Metadatas[plugin].FilePath);
            }
        }
        internal static void LoadPlugin(PluginLoadContext context, string pluginPath)
        {
            try
            {
                var assembly = context.LoadFromStream(PluginLoadContext.LoadStreamIndependent(pluginPath));

                var type = assembly.GetExportedTypes().FirstOrDefault(x => typeof(IPlugin).IsAssignableFrom(x));
                if (type is null)
                {
                    context.Unload();
                    return;
                }
                if (Activator.CreateInstance(type) is IPlugin plugin)
                {
                    // 插件没有指定目标，或者插件目标兼容当前的目标，或当前没有设置任何目标才注册
                    if (plugin.Targets.Length == 0 || plugin.Targets.Intersect(Config.Repository.Targets).Any() || Config.Repository.Targets.Count == 0)
                    {
                        RegisterHandlers(plugin);
                    }
                    Assemblies.Add(assembly);
                }
                else
                {
                    FailedPlugins.Add(pluginPath);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                FailedPlugins.Add(pluginPath);
            }
        }
        internal static void RegisterHandlers(IPlugin plugin)
        {
            Directory.CreateDirectory(@$"./PluginData/{plugin.Name}");
            LoadedPlugins.Add(plugin);
            var settings = plugin.GetType().GetProperties().Where(x => x.GetCustomAttribute<PluginSettingAttribute>() != default);
            if (Config.Plugin.PluginSettings.TryGetValue(plugin.Name, out var config))
            {
                foreach (var setting in settings)
                {
                    if (config.TryGetValue(setting.Name, out var value))
                    {
                        if (value is string str && setting.PropertyType != typeof(string))
                        { // Yaml会把object value=1;反序列化成"1"
                            if (setting.PropertyType == typeof(int))
                            {
                                value = int.Parse(str);
                            }
                            else if (setting.PropertyType == typeof(bool))
                            {
                                value = bool.Parse(str);
                            }
                            else if (setting.PropertyType == typeof(double))
                            {
                                value = double.Parse(str);
                            }
                        }
                        setting.SetValue(plugin, value);
                    }
                }
            }
            else
            {
                Config.Plugin.PluginSettings.Add(plugin.Name, []);
                foreach (var setting in settings)
                {
                    Config.Plugin.PluginSettings[plugin.Name].Add(setting.Name, setting.GetValue(plugin)!);
                }
            }
            foreach (var method in plugin.GetType().GetMethods())
            {
                var analyzerAttribute = method.GetCustomAttribute<AnalyzerAttribute>();
                if (analyzerAttribute != default)
                {
                    if (analyzerAttribute.Response)
                    {
                        if (!ResponseAnalyzerMethods.ContainsKey(analyzerAttribute.Priority))
                            ResponseAnalyzerMethods.Add(analyzerAttribute.Priority, []);
                        ResponseAnalyzerMethods[analyzerAttribute.Priority].Add((plugin, method));
                    }
                    else
                    {
                        if (!RequsetAnalyzerMethods.ContainsKey(analyzerAttribute.Priority))
                            RequsetAnalyzerMethods.Add(analyzerAttribute.Priority, []);
                        RequsetAnalyzerMethods[analyzerAttribute.Priority].Add((plugin, method));
                    }
                }
            }
        }

        internal class PluginLoadContext(string contextName) : AssemblyLoadContext(contextName, true)
        {
            protected override Assembly? Load(AssemblyName assemblyName)
            {
                var sharedAssembly = Assemblies.FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
                if (sharedAssembly != null) return sharedAssembly;

                var hostAssembly = Default.Assemblies
                    .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
                if (hostAssembly != null) return hostAssembly;

                var localPath = Metadatas.FirstOrDefault(x => x.Value.FilePath.EndsWith($"{assemblyName.Name}.dll")).Value?.FilePath;

                return File.Exists(localPath) ? LoadFromStream(LoadStreamIndependent(localPath)) : null;
            }

            internal static MemoryStream LoadStreamIndependent(string path)
            {
                var ms = new MemoryStream();
                // 可能不需要加FileShare？或者不需要再弄个MS？
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                fs.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }
        }
        internal class PluginMetadata(string path, string name, bool loadInHost, List<string> sharedContextsWith)
        {
            public string FilePath { get; } = path;
            public string PluginName { get; } = name;
            public bool LoadInHost { get; } = loadInHost;
            public List<string> SharedContextsWith { get; } = sharedContextsWith;
        }
    }
}
