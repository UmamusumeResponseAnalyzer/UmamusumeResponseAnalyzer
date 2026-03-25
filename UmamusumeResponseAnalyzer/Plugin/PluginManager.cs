using Spectre.Console;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using WatsonWebserver.Core;

namespace UmamusumeResponseAnalyzer.Plugin
{
    public static class PluginManager
    {
        private static Dictionary<string, PluginMetadata> Metadatas { get; } = [];
        internal static List<string> FailedPlugins { get; } = [];
        public static List<IPlugin> LoadedPlugins { get; } = [];
        internal static SortedDictionary<int, List<(IPlugin Self, MethodInfo Method)>> RequestAnalyzerMethods { get; } = [];
        internal static SortedDictionary<int, List<(IPlugin Self, MethodInfo Method)>> ResponseAnalyzerMethods { get; } = [];
        internal static List<HashSet<string>> ContextGroups { get; } = [];
        internal static Dictionary<string, PluginLoadContext> Contexts { get; } = [];
        internal static Dictionary<string, Assembly> AssemblyMap { get; } = [];
        internal static List<Assembly> Assemblies { get; } = [];
        private static HashSet<string> HostAssemblyNames { get; } = [];

        internal static void Init()
        {
            Directory.CreateDirectory("Plugins");
            LoadMetadatas();
            BuildGroups();
            LoadPlugins();
        }

        internal static void LoadMetadatas()
        {
            var culture = LanguageConfig.GetCulture();
            var pluginsDir = new DirectoryInfo("Plugins");

            foreach (var dll in pluginsDir.GetFiles("*.dll", SearchOption.AllDirectories))
            {
                if (dll.Name.EndsWith(".resources.dll") && !dll.FullName.Contains(culture)) continue;
                var metadata = LoadMetadata(dll.FullName, null, false);
                Metadatas[metadata.PluginName] = metadata;
            }

            foreach (var zip in pluginsDir.GetFiles("*.zip", SearchOption.TopDirectoryOnly).Select(x => x.FullName))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(zip);
                    // 收集需要后处理的卫星资源条目
                    List<ZipArchiveEntry> satelliteEntries = [];

                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                        if (!entry.FullName.Contains('/'))
                        {
                            // 主dll（根目录下）
                            using var stream = entry.Open();
                            var ms = new MemoryStream();
                            stream.CopyTo(ms);
                            ms.Position = 0;

                            try
                            {
                                var metadata = LoadMetadata($"{zip}|{entry.FullName}", ms, true);
                                Metadatas[metadata.PluginName] = metadata;
                            }
                            catch { }
                        }
                        else if (entry.FullName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase) &&
                                 entry.FullName.Contains(culture, StringComparison.OrdinalIgnoreCase))
                        {
                            // 卫星资源文件（子目录下），延迟处理以确保主dll元数据已加载
                            satelliteEntries.Add(entry);
                        }
                    }

                    // 关联卫星资源到对应的主插件元数据
                    foreach (var entry in satelliteEntries)
                    {
                        var resourceName = Path.GetFileNameWithoutExtension(entry.FullName);
                        if (resourceName.EndsWith(".resources"))
                        {
                            var assemblyName = resourceName[..^".resources".Length];
                            if (Metadatas.TryGetValue(assemblyName, out var metadata))
                            {
                                metadata.SatelliteEntries.Add(entry.FullName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.WriteException(ex);
                }
            }
        }

        internal static PluginMetadata LoadMetadata(string path, Stream? stream, bool isFromZip)
        {
            var tempContext = new PluginLoadContext("temp");
            var assembly = stream != null ? tempContext.LoadFromStream(stream) : tempContext.LoadFromAssemblyPath(path);
            var loadInHost = assembly.GetCustomAttribute<LoadInHostContextAttribute>() != null;
            var sharedWith = assembly.GetCustomAttributes<SharedContextWithAttribute>().SelectMany(x => x.PluginNames).ToList();
            var metadata = new PluginMetadata(path, assembly.GetName().Name ?? string.Empty, loadInHost, sharedWith, isFromZip);
            tempContext.Unload();
            return metadata;
        }

        internal static void BuildGroups()
        {
            var pluginToGroup = new Dictionary<string, HashSet<string>>();

            foreach (var m in Metadatas.Values.Where(x => x.SharedContextsWith.Count != 0))
            {
                m.SharedContextsWith.RemoveAll(x => Metadatas.TryGetValue(x, out var v) && v.LoadInHost);
                foreach (var share in m.SharedContextsWith)
                {
                    if (pluginToGroup.TryGetValue(share, out var group))
                    {
                        group.Add(m.PluginName);
                        pluginToGroup[m.PluginName] = group;
                    }
                    else
                    {
                        var newGroup = new HashSet<string> { share, m.PluginName };
                        ContextGroups.Add(newGroup);
                        pluginToGroup[share] = newGroup;
                        pluginToGroup[m.PluginName] = newGroup;
                    }
                }
            }

            foreach (var m in Metadatas.Values.Where(x => !x.LoadInHost && x.SharedContextsWith.Count == 0))
            {
                if (!pluginToGroup.ContainsKey(m.PluginName))
                    ContextGroups.Add([m.PluginName]);
            }
        }

        internal static void LoadPlugins()
        {
            foreach (var a in AssemblyLoadContext.Default.Assemblies)
            {
                var name = a.GetName().Name;
                if (name != null) HostAssemblyNames.Add(name);
            }

            foreach (var m in Metadatas.Values.Where(x => x.LoadInHost))
            {
                LoadIntoContext(AssemblyLoadContext.Default, m);
            }

            foreach (var group in ContextGroups)
            {
                var key = string.Join("&", group);
                if (!Contexts.TryGetValue(key, out var ctx))
                {
                    ctx = new PluginLoadContext(key);
                    Contexts[key] = ctx;
                }
                foreach (var name in group)
                    LoadIntoContext(ctx, Metadatas[name]);
            }
        }

        internal static void LoadIntoContext(AssemblyLoadContext ctx, PluginMetadata m)
        {
            try
            {
                using var stream = CreateStream(m);
                var assembly = ctx.LoadFromStream(stream);

                var type = assembly.GetExportedTypes().FirstOrDefault(x => typeof(IPlugin).IsAssignableFrom(x));
                if (type == null) return;

                if (Activator.CreateInstance(type) is not IPlugin plugin)
                {
                    FailedPlugins.Add(m.FilePath);
                    return;
                }

                if (plugin.Targets.Length == 0 || plugin.Targets.Intersect(Config.Repository.Targets).Any() || Config.Repository.Targets.Count == 0)
                {
                    Directory.CreateDirectory($"./PluginData/{plugin.Name}");
                    LoadedPlugins.Add(plugin);
                    PluginSettingsManager.LoadSettings(plugin);
                    RegisterMethods(plugin);
                }

                var assemblyName = assembly.GetName().Name;
                if (assemblyName != null) AssemblyMap[assemblyName] = assembly;
                Assemblies.Add(assembly);

                if (m.LoadInHost)
                {
                    foreach (var r in assembly.GetReferencedAssemblies())
                    {
                        if (r.Name != null && Metadatas.TryGetValue(r.Name, out var dep) &&
                            !HostAssemblyNames.Contains(r.Name))
                        {
                            using var s = CreateStream(dep);
                            var loaded = AssemblyLoadContext.Default.LoadFromStream(s);
                            HostAssemblyNames.Add(r.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                FailedPlugins.Add(m.FilePath);
            }
        }

        internal static Stream CreateStream(PluginMetadata m)
        {
            if (!m.IsFromZip)
                return PluginLoadContext.LoadFileStream(m.FilePath);

            var parts = m.FilePath.Split('|', 2);
            using var archive = ZipFile.OpenRead(parts[0]);
            var entry = archive.GetEntry(parts[1])!;
            var ms = new MemoryStream();
            using (var s = entry.Open()) s.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        internal static void RegisterMethods(IPlugin plugin)
        {
            foreach (var method in plugin.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var analyzer = method.GetCustomAttribute<AnalyzerAttribute>();
                if (analyzer != null)
                {
                    var dict = analyzer.Response ? ResponseAnalyzerMethods : RequestAnalyzerMethods;
                    if (!dict.TryGetValue(analyzer.Priority, out var list))
                    {
                        list = [];
                        dict[analyzer.Priority] = list;
                    }
                    list.Add((plugin, method));
                    continue;
                }

                var route = method.GetCustomAttribute<RouteAttribute>();
                if (route != null)
                {
                    var ps = method.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(HttpContextBase))
                    {
                        Server.Instance.Routes.PreAuthentication.Static.Add(route.Method, $"/{plugin.Name}/{route.Path}", x => (Task)method.Invoke(null, [x])!);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"{plugin.Name.EscapeMarkup()}注册Route{route.Path.EscapeMarkup()}失败:参数有且只能有一个HttpContextBase，实际为{string.Join(", ", ps.Select(x => x.ParameterType.Name)).EscapeMarkup()}");
                    }
                }
            }
        }

        internal class PluginLoadContext(string name) : AssemblyLoadContext(name, true)
        {
            protected override Assembly? Load(AssemblyName name)
            {
                if (name.Name != null && AssemblyMap.TryGetValue(name.Name, out var shared))
                    return shared;

                if (name.Name != null && HostAssemblyNames.Contains(name.Name))
                {
                    var host = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
                    if (host != null) return host;
                }

                // 处理卫星资源文件加载（从zip中）
                if (!string.IsNullOrEmpty(name.CultureName) && name.Name != null && name.Name.EndsWith(".resources"))
                {
                    var searchSuffix = $"{name.CultureName}/{name.Name}.dll";
                    foreach (var metadata in Metadatas.Values.Where(m => m.IsFromZip))
                    {
                        var satelliteEntry = metadata.SatelliteEntries.FirstOrDefault(e =>
                            e.Contains(searchSuffix, StringComparison.OrdinalIgnoreCase));

                        if (satelliteEntry != null)
                        {
                            var zipPath = metadata.FilePath.Split('|', 2)[0];
                            using var satelliteStream = CreateZipEntryStream(zipPath, satelliteEntry);
                            if (satelliteStream != null) return LoadFromStream(satelliteStream);
                        }
                    }
                    return null;
                }

                if (name.Name == null || !Metadatas.TryGetValue(name.Name, out var dep)) return null;

                using var stream = CreateStream(dep);
                return LoadFromStream(stream);
            }

            private static MemoryStream? CreateZipEntryStream(string zipPath, string entryName)
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.GetEntry(entryName);
                if (entry == null) return null;

                var ms = new MemoryStream();
                using (var s = entry.Open()) s.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }

            internal static MemoryStream LoadFileStream(string path)
            {
                var ms = new MemoryStream();
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                fs.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }
        }

        internal class PluginMetadata(string path, string name, bool loadInHost, List<string> shared, bool isFromZip)
        {
            public string FilePath { get; } = path;
            public string PluginName { get; } = name;
            public bool LoadInHost { get; } = loadInHost;
            public List<string> SharedContextsWith { get; } = shared;
            public bool IsFromZip { get; } = isFromZip;
            public List<string> SatelliteEntries { get; } = [];
        }
    }
}
